using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Search;

/// <summary>
/// An <see cref="IPlayerAgent"/> for the "searched" seat in a Monte-Carlo
/// simulation. Intercepts the three combat/priority decision methods
/// (<see cref="DeclareAttackersAsync"/>, <see cref="DeclareBlockersAsync"/>,
/// <see cref="ChoosePriorityActionAsync"/>) and operates in one of two modes:
///
/// <list type="bullet">
///   <item>
///     <term>Script replay</term>
///     <description>
///       A pre-loaded queue of <see cref="SimMove"/>s answers the first
///       decisions without pausing — used to replay a tree path to a target
///       node. While the script is non-empty each decision pops the next move
///       and returns it immediately.
///     </description>
///   </item>
///   <item>
///     <term>Capture (live pause)</term>
///     <description>
///       When the script is empty the agent suspends the engine at the next
///       decision: it publishes the <see cref="SimDecision"/> (completing
///       <see cref="NextDecisionAsync"/>) and then awaits a
///       <see cref="SupplyMove"/> call from the search loop before letting
///       the engine continue.
///     </description>
///   </item>
/// </list>
///
/// All non-searched prompts (mulligan, targets, scry, …) delegate to an
/// internal <see cref="DeterministicBotAgent"/> so games still run to
/// completion.
///
/// <para>
/// NOTE — Priority enumeration (Task A3 scope): <see cref="ChoosePriorityActionAsync"/>
/// exposes a Pass-only legal set for now. Full priority move enumeration is
/// deferred to Task B1.
/// </para>
/// </summary>
public sealed class SearchAgent : IPlayerAgent
{
    private readonly Player _seat;

    /// <summary>
    /// Fallback for non-searched prompts (mulligan, targets, scry, …).
    /// Built over the SAME cloned player so it can inspect hand / library.
    /// </summary>
    private readonly IPlayerAgent _fallback;

    /// <summary>
    /// Pre-loaded moves that replay a known tree path without pausing.
    /// Dequeued one per intercepted decision until empty, at which point the
    /// agent switches to live-capture mode.
    /// </summary>
    private readonly Queue<SimMove> _script;

    // ── TCS pair ─────────────────────────────────────────────────────────────
    // RunContinuationsAsynchronously on BOTH is essential: prevents the engine
    // continuation from running inline on the search thread and vice-versa,
    // which would deadlock because one thread holds the async pump the other needs.
    //
    // SEQUENCING INVARIANT:
    //   _decisionReady is swapped to a fresh pending TCS BEFORE the previous one
    //   is completed. This guarantees that by the time the search-side continuation
    //   wakes and calls NextDecisionAsync() again, it reads the NEW pending task —
    //   never a stale completed one. (If we completed first and reset second the
    //   search side would race and see the already-completed task again.)

    private TaskCompletionSource<SimDecision> _decisionReady = New<SimDecision>();
    private TaskCompletionSource<SimMove> _moveSupplied = New<SimMove>();

    private static TaskCompletionSource<T> New<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ── CombatSearch topology cap (mirrors CombatSearch.TopKAttackers) ───────
    // Bound the attacker-subset enumeration to the same cap used by the
    // CombatSearch greedy pass so the legal-move list is tractable even on
    // large boards.  2^TopK = 256 max subsets.
    private const int TopKAttackers = 8;

    /// <summary>
    /// Optional rollout strategy. When non-null and the script is exhausted,
    /// decisions are forwarded to this strategy (rollout mode) rather than
    /// pausing for a <see cref="SupplyMove"/> call. Capture mode is used when
    /// this is null (original A3 behaviour).
    /// </summary>
    private readonly IBotStrategy? _rolloutStrategy;

    /// <summary>
    /// Construct a <see cref="SearchAgent"/> for the given cloned seat.
    /// The fallback is built internally; optionally supply a pre-loaded
    /// script for tree-path replay.
    /// </summary>
    public SearchAgent(Player seat, IEnumerable<SimMove>? script = null)
        : this(seat, script, rolloutStrategy: null)
    {
    }

    /// <summary>
    /// Internal constructor that also accepts a rollout strategy. Called by
    /// <see cref="EngineSimulator"/> to wire rollout mode without exposing the
    /// internal <see cref="IBotStrategy"/> type on the public API.
    /// </summary>
    internal SearchAgent(
        Player seat,
        IEnumerable<SimMove>? script,
        IBotStrategy? rolloutStrategy)
    {
        _seat = seat ?? throw new ArgumentNullException(nameof(seat));
        _fallback = new DeterministicBotAgent();
        _script = script != null ? new Queue<SimMove>(script) : new Queue<SimMove>();
        _rolloutStrategy = rolloutStrategy;
    }

    // ── Search-side API ───────────────────────────────────────────────────────

    /// <summary>
    /// Await the next decision the engine asks this seat.
    /// Returns when <see cref="DeclareAttackersAsync"/>,
    /// <see cref="DeclareBlockersAsync"/>, or
    /// <see cref="ChoosePriorityActionAsync"/> enters capture mode (script
    /// empty).
    /// </summary>
    public Task<SimDecision> NextDecisionAsync() => _decisionReady.Task;

    /// <summary>
    /// Answer the pending decision; the engine resumes and advances until the
    /// next captured decision (or game over).
    /// </summary>
    public void SupplyMove(SimMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        _moveSupplied.TrySetResult(move);
    }

    // ── Internal TCS mechanic ─────────────────────────────────────────────────

    /// <summary>
    /// Core suspend/resume handler. If the script still has moves, pops and
    /// returns immediately. If a rollout strategy is configured, delegates
    /// the decision to that strategy (plays out without pausing). Otherwise
    /// publishes the decision to the search loop and suspends until
    /// <see cref="SupplyMove"/> is called.
    ///
    /// Sequencing: the TCS pair is rotated BEFORE the previous decision TCS
    /// is completed (not after), so that by the time the search-side continuation
    /// runs and calls <see cref="NextDecisionAsync"/> again, <c>_decisionReady</c>
    /// is already pointing at a fresh pending task. This avoids the race where the
    /// search grabs the stale completed task on a tight re-loop.
    /// </summary>
    private async Task<SimMove> DecideAsync(SimDecision decision, CancellationToken ct)
    {
        if (_script.Count > 0)
        {
            // Script replay — no pause, engine continues uninterrupted.
            return _script.Dequeue();
        }

        // Rollout mode: script exhausted and a strategy was provided — use it
        // to pick a move without pausing the engine.
        if (_rolloutStrategy != null)
        {
            return PickRolloutMove(decision);
        }

        // Capture mode:
        // 1. Swap in fresh TCS FIRST (so NextDecisionAsync returns the new
        //    pending task the moment the search side re-enters the loop).
        var decisionTcs = _decisionReady;
        _decisionReady = New<SimDecision>();
        var moveTcs = New<SimMove>();
        _moveSupplied = moveTcs;

        // 2. Signal the search loop with the completed decision.
        decisionTcs.TrySetResult(decision);

        // 3. Register cancellation so the engine isn't stranded when the
        //    search is abandoned.
        using var reg = ct.Register(() => moveTcs.TrySetCanceled(ct));

        // 4. Await the search loop's move reply.
        return await moveTcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Picks a rollout move from the decision's legal moves using the rollout
    /// strategy as a tie-breaker signal. Simply selects the first non-empty
    /// attack / non-pass move when available, mirroring the heuristic spirit
    /// of the rollout strategy without needing a full GameContext here (we don't
    /// have one at this call site). For attacker decisions, any all-out move is
    /// preferred; for blockers the first move suffices; priority always passes.
    /// </summary>
    private SimMove PickRolloutMove(SimDecision decision)
    {
        return decision.Kind switch
        {
            SimDecisionKind.DeclareAttackers =>
                // Prefer all-out attack, then any attack, then pass.
                decision.LegalMoves.FirstOrDefault(m => m.IsAllOutAttack)
                ?? decision.LegalMoves.FirstOrDefault(m => !m.IsEmptyAttack)
                ?? decision.LegalMoves[0],

            SimDecisionKind.DeclareBlockers =>
                // Use greedy block (first non-empty plan) when available.
                decision.LegalMoves.FirstOrDefault(m => m.BlockPlan?.Blockers.Count > 0)
                ?? decision.LegalMoves[0],

            SimDecisionKind.Priority =>
                // Always pass in rollout (priority enumeration is B1 scope).
                decision.LegalMoves.First(m => m.IsPass),

            _ => decision.LegalMoves[0]
        };
    }

    // ── Intercepted decision methods ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CombatPlan> DeclareAttackersAsync(
        GameContext ctx,
        IReadOnlyList<Creature> eligibleAttackers,
        CancellationToken ct = default)
    {
        // Rollout mode short-circuit: script empty + strategy set → delegate to
        // the heuristic strategy with the live context (no pause, no TCS).
        if (_script.Count == 0 && _rolloutStrategy != null)
            return _rolloutStrategy.PickAttackers(ctx, _seat, eligibleAttackers);

        // Build legal attacker-subset moves. Always include the empty-attack
        // move (pass) first.
        var moves = BuildAttackerMoves(ctx, eligibleAttackers);
        var decision = new SimDecision(SimDecisionKind.DeclareAttackers, moves);

        var chosen = await DecideAsync(decision, ct).ConfigureAwait(false);

        // Materialize the CombatPlan. The scripted plan may reference
        // creatures from a different sandbox (e.g., the Advance sandbox
        // that was used to generate the move list). Re-map the plan to
        // THIS sandbox's creatures via InstanceId, which is stable across
        // clones (GameStateCloner preserves InstanceId).
        var rawPlan = chosen.CombatPlan;
        if (rawPlan == null || rawPlan.Attackers.Count == 0)
            return CombatPlan.None;

        return RemapCombatPlan(rawPlan, eligibleAttackers, ctx);
    }

    /// <summary>
    /// Re-maps a <see cref="CombatPlan"/> from a foreign sandbox to the
    /// current sandbox by matching <see cref="Creature.InstanceId"/> against
    /// <paramref name="eligibleAttackers"/> (current-sandbox creatures) and
    /// finding the current-sandbox defender via <c>ctx.AllPlayers</c> by Id.
    /// Any attacker not found in the current sandbox (or with a foreign
    /// defender reference) is silently dropped — this is safe because the
    /// cloner preserves InstanceId so a matching attacker WILL be found if
    /// the creature is still on the battlefield.
    /// </summary>
    private static CombatPlan RemapCombatPlan(
        CombatPlan foreignPlan,
        IReadOnlyList<Creature> eligibleAttackers,
        GameContext ctx)
    {
        // Build a lookup from InstanceId to current-sandbox creature.
        var byId = eligibleAttackers.ToDictionary(c => c.InstanceId);

        var remapped = new List<AttackerDeclaration>(foreignPlan.Attackers.Count);
        foreach (var decl in foreignPlan.Attackers)
        {
            if (!byId.TryGetValue(decl.Attacker.InstanceId, out var localAttacker))
                continue; // creature not in this sandbox (shouldn't happen, but safe)

            // Find the defender by Player.Id in ctx.AllPlayers.
            object? defender = decl.DefendingPlayerOrPlaneswalker switch
            {
                Player p => ctx.AllPlayers.FirstOrDefault(cp => cp.Id == p.Id),
                _ => null // planeswalkers not supported in this simplification
            };
            if (defender == null)
                defender = ctx.AllPlayers.First(p => !ReferenceEquals(p, ctx.Self));

            remapped.Add(new AttackerDeclaration(localAttacker, defender));
        }

        return remapped.Count == 0 ? CombatPlan.None : new CombatPlan(remapped);
    }

    /// <inheritdoc/>
    public async Task<BlockPlan> DeclareBlockersAsync(
        GameContext ctx,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligibleBlockers,
        CancellationToken ct = default)
    {
        // Rollout mode short-circuit.
        if (_script.Count == 0 && _rolloutStrategy != null)
            return _rolloutStrategy.PickBlockers(ctx, _seat, attackers, eligibleBlockers);

        var moves = BuildBlockerMoves(ctx, attackers, eligibleBlockers);
        var decision = new SimDecision(SimDecisionKind.DeclareBlockers, moves);

        var chosen = await DecideAsync(decision, ct).ConfigureAwait(false);

        return chosen.BlockPlan ?? BlockPlan.None;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Priority enumeration is Pass-only in Task A3. Full enumeration (spell
    /// casts, land plays, activated abilities) is Task B1.
    ///
    /// Priority decisions do NOT consume script moves — the script is keyed
    /// on substantive decisions (DeclareAttackers / DeclareBlockers). A
    /// Priority call while the script is non-empty means we are still
    /// replaying the path to a target node; we auto-pass so that combat /
    /// main-phase decisions can be reached without consuming the scripted
    /// non-Priority move prematurely.
    /// </remarks>
    public async Task<PriorityAction> ChoosePriorityActionAsync(
        GameContext ctx,
        CancellationToken ct = default)
    {
        // Rollout mode short-circuit: delegate to strategy.
        // Strategy picks the best priority action without pausing.
        if (_script.Count == 0 && _rolloutStrategy != null)
            return _rolloutStrategy.PickPriorityAction(ctx, _seat);

        // If the script is non-empty, the next move in the script is for a
        // DeclareAttackers or DeclareBlockers decision, NOT for Priority.
        // Auto-pass so we don't consume the script move prematurely.
        if (_script.Count > 0)
            return PriorityAction.Pass;

        // Pass-only capture mode for now (Task B1 will enumerate spells/lands/abilities).
        var passMove = SimMove.FromPriorityAction(PriorityAction.Pass);
        var decision = new SimDecision(
            SimDecisionKind.Priority,
            new[] { passMove });

        var chosen = await DecideAsync(decision, ct).ConfigureAwait(false);

        return chosen.PriorityAction ?? PriorityAction.Pass;
    }

    // ── Non-searched prompts — delegate to fallback ───────────────────────────

    public Task<MulliganDecision> ChooseMulliganAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken,
        CancellationToken ct = default)
        => _fallback.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom,
        CancellationToken ct = default)
        => _fallback.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(
        GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => _fallback.ChooseTargetsAsync(ctx, request, ct);

    public Task<int> ChooseXAsync(
        GameContext ctx, ICard source, CancellationToken ct = default)
        => _fallback.ChooseXAsync(ctx, source, ct);

    public Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default)
        => _fallback.ChooseModeAsync(ctx, modes, modeIntents, ct);

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
        GameContext ctx, IReadOnlyList<ITriggeredAbility> mine,
        CancellationToken ct = default)
        => _fallback.OrderTriggersAsync(ctx, mine, ct);

    public Task<ManaPayment> ChooseManaSourcesAsync(
        GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => _fallback.ChooseManaSourcesAsync(ctx, cost, ct);

    public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => _fallback.ChooseScryDecisionAsync(ctx, peeked, ct);

    public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => _fallback.ChooseSurveilDecisionAsync(ctx, peeked, ct);

    // ── Legal-move builders ───────────────────────────────────────────────────

    private static List<SimMove> BuildAttackerMoves(
        GameContext ctx,
        IReadOnlyList<Creature> eligibleAttackers)
    {
        // Filter to creatures that CAN contribute offensively (power > 0 is the
        // same heuristic CombatSearch uses to avoid pointless 0-power attackers).
        // We still include the empty-attack unconditionally (always legal).
        var usable = eligibleAttackers
            .Where(c => c.Power > 0)
            .ToList();

        if (usable.Count > TopKAttackers)
            usable = usable.OrderByDescending(c => c.Power).Take(TopKAttackers).ToList();

        // Determine the defender (first non-self player in the context).
        var defender = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, ctx.Self))
                       ?? ctx.AllPlayers[0];

        var totalEligible = usable.Count;
        var moves = new List<SimMove>(1 << Math.Min(totalEligible, 10));

        // Empty attack (always the first move so IsEmptyAttack is easy to find).
        moves.Add(SimMove.FromCombatPlan(CombatPlan.None, eligibleCount: totalEligible));

        // Enumerate all non-empty attacker subsets (mirrors CombatSearch.SearchSubsets).
        var n = usable.Count;
        for (long mask = 1; mask < (1L << n); mask++)
        {
            var subset = new List<AttackerDeclaration>(n);
            for (var i = 0; i < n; i++)
            {
                if ((mask & (1L << i)) != 0)
                    subset.Add(new AttackerDeclaration(usable[i], defender));
            }
            var plan = new CombatPlan(subset);
            moves.Add(SimMove.FromCombatPlan(plan, eligibleCount: totalEligible));
        }

        return moves;
    }

    // ── Block-move enumeration cap ────────────────────────────────────────────
    // Single-blocker-per-attacker assignments: O(attackers × blockers).
    // Gang-block pairs (two blockers on one attacker): O(attackers × C(blockers,2)).
    // The cap below limits the total produced so the legal-move list stays tractable
    // even on large boards. 50 is generous: 2 attackers × 5 blockers = 10 singles +
    // 2 × C(5,2) = 20 pairs + 1 no-block = 31 — well under the cap. The greedy plan
    // is always included so the search always has at least one "smart" option.
    private const int MaxBlockMoves = 50;

    /// <summary>
    /// Enumerate a bounded, diverse set of legal block assignments for MCTS:
    ///
    /// <list type="number">
    ///   <item>No-block (always first).</item>
    ///   <item>
    ///     Every 1-to-1 assignment (each eligible blocker on each attacker),
    ///     including chump blocks (blocker dies) and trades — not only hard-blocks
    ///     where the blocker survives. This is the key enrichment over the old
    ///     greedy-only enumeration.
    ///   </item>
    ///   <item>
    ///     A bounded set of 2-blocker gang assignments (two blockers on the same
    ///     attacker), useful for trading a small gang into a large attacker.
    ///     Gang blocks are enumerated attacker-by-attacker, pair-by-pair, and
    ///     stop as soon as <see cref="MaxBlockMoves"/> is reached.
    ///   </item>
    ///   <item>
    ///     The greedy CombatPolicy plan as a heuristic "smart" anchor — always
    ///     included when non-empty (it may already appear from the 1-to-1 sweep
    ///     but is de-duplicated by Key).
    ///   </item>
    /// </list>
    ///
    /// De-duplication is by <see cref="SimMove.Key"/> to avoid feeding the same
    /// plan to MCTS under two different nodes.
    ///
    /// Cap: <see cref="MaxBlockMoves"/>. Enumeration terminates once the cap is
    /// reached, so the number of options is always bounded regardless of board size.
    /// </summary>
    private static List<SimMove> BuildBlockerMoves(
        GameContext ctx,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligibleBlockers)
    {
        // Start with "no block" (always legal and always first).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var moves = new List<SimMove>();

        void TryAdd(SimMove m)
        {
            if (moves.Count < MaxBlockMoves && seen.Add(m.Key))
                moves.Add(m);
        }

        TryAdd(SimMove.FromBlockPlan(BlockPlan.None));

        if (attackers.Count == 0 || eligibleBlockers.Count == 0)
            return moves;

        // ── 1-to-1 assignments: every eligible blocker on every attacker ─────
        // Includes chump blocks (blocker dies) and trades — not just hard-blocks.
        // This is the primary enrichment: the old greedy-only enumerator only added
        // survive-filtered blocks; here ANY assignment is explored.
        foreach (var att in attackers)
        {
            foreach (var blk in eligibleBlockers)
            {
                var plan = new BlockPlan(new[] { new BlockerDeclaration(blk, att) });
                TryAdd(SimMove.FromBlockPlan(plan));
                if (moves.Count >= MaxBlockMoves) goto done;
            }
        }

        // ── 2-blocker gang assignments (one attacker, two blockers) ──────────
        // Enumerate blocker pairs for each attacker. Useful when two small
        // blockers can gang-kill a large attacker that neither can handle alone.
        // Bounded: at most C(eligibleBlockers.Count, 2) pairs per attacker.
        foreach (var att in attackers)
        {
            for (int i = 0; i < eligibleBlockers.Count; i++)
            {
                for (int j = i + 1; j < eligibleBlockers.Count; j++)
                {
                    var plan = new BlockPlan(new[]
                    {
                        new BlockerDeclaration(eligibleBlockers[i], att),
                        new BlockerDeclaration(eligibleBlockers[j], att),
                    });
                    TryAdd(SimMove.FromBlockPlan(plan));
                    if (moves.Count >= MaxBlockMoves) goto done;
                }
            }
        }

        done:

        // ── Greedy CombatPolicy plan as heuristic anchor ─────────────────────
        // Always include the greedy plan (if non-empty) so the search has at
        // least one "smart" heuristic option even when the cap is reached.
        // De-duplication via seen ensures no double-counting.
        var policy = new CombatPolicy(ArchetypeWeights.Default);
        var greedyPlan = policy.PickBlockers(ctx, ctx.Self, attackers, eligibleBlockers);
        if (greedyPlan.Blockers.Count > 0)
        {
            var greedyMove = SimMove.FromBlockPlan(greedyPlan);
            if (seen.Add(greedyMove.Key))
                moves.Add(greedyMove);
        }

        return moves;
    }
}
