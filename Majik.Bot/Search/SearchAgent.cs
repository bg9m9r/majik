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
    /// Construct a <see cref="SearchAgent"/> for the given cloned seat.
    /// The fallback is built internally; optionally supply a pre-loaded
    /// script for tree-path replay.
    /// </summary>
    public SearchAgent(Player seat, IEnumerable<SimMove>? script = null)
    {
        _seat = seat ?? throw new ArgumentNullException(nameof(seat));
        _fallback = new DeterministicBotAgent();
        _script = script != null ? new Queue<SimMove>(script) : new Queue<SimMove>();
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
    /// returns immediately. Otherwise publishes the decision to the search loop
    /// and suspends until <see cref="SupplyMove"/> is called.
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

    // ── Intercepted decision methods ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CombatPlan> DeclareAttackersAsync(
        GameContext ctx,
        IReadOnlyList<Creature> eligibleAttackers,
        CancellationToken ct = default)
    {
        // Build legal attacker-subset moves. Always include the empty-attack
        // move (pass) first.
        var moves = BuildAttackerMoves(ctx, eligibleAttackers);
        var decision = new SimDecision(SimDecisionKind.DeclareAttackers, moves);

        var chosen = await DecideAsync(decision, ct).ConfigureAwait(false);

        // Materialize the CombatPlan. The move must carry a plan; if somehow
        // it doesn't (shouldn't happen with correct search code) fall through
        // to no-attack as a safe default.
        return chosen.CombatPlan ?? CombatPlan.None;
    }

    /// <inheritdoc/>
    public async Task<BlockPlan> DeclareBlockersAsync(
        GameContext ctx,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligibleBlockers,
        CancellationToken ct = default)
    {
        var moves = BuildBlockerMoves(ctx, attackers, eligibleBlockers);
        var decision = new SimDecision(SimDecisionKind.DeclareBlockers, moves);

        var chosen = await DecideAsync(decision, ct).ConfigureAwait(false);

        return chosen.BlockPlan ?? BlockPlan.None;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Priority enumeration is Pass-only in Task A3. Full enumeration (spell
    /// casts, land plays, activated abilities) is Task B1.
    /// </remarks>
    public async Task<PriorityAction> ChoosePriorityActionAsync(
        GameContext ctx,
        CancellationToken ct = default)
    {
        // Pass-only for now (Task B1 will enumerate spells/lands/abilities).
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

    private static List<SimMove> BuildBlockerMoves(
        GameContext ctx,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligibleBlockers)
    {
        // Start with "no block" (always legal).
        var moves = new List<SimMove> { SimMove.FromBlockPlan(BlockPlan.None) };

        // Use CombatPolicy's greedy block-picker as a representative single move.
        // Full block enumeration is a task-B1 concern (block enumeration is O(n^m));
        // here we surface one heuristic block plan so the search has a non-trivial
        // alternative to no-block without blowing up the legal-move count.
        if (attackers.Count > 0 && eligibleBlockers.Count > 0)
        {
            var policy = new CombatPolicy(ArchetypeWeights.Default);
            var greedyPlan = policy.PickBlockers(ctx, ctx.Self, attackers, eligibleBlockers);
            if (greedyPlan.Blockers.Count > 0)
                moves.Add(SimMove.FromBlockPlan(greedyPlan));
        }

        return moves;
    }
}
