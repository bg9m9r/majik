using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Strategies;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Search;

/// <summary>
/// MCTS-backed <see cref="IBotStrategy"/>. Uses <see cref="Mcts"/> to search
/// the best combat decision (DeclareAttackers / DeclareBlockers) and delegates
/// every other prompt to the inner <see cref="HeuristicStrategy"/>.
///
/// <para>
/// <b>InstanceId remap:</b> The MCTS search operates over sandbox clones of the
/// live game state, so the <see cref="SimMove"/> chosen by the search references
/// cloned <see cref="Creature"/> objects. Before returning a plan to the engine
/// these are translated back to the live engine objects by matching
/// <see cref="Creature.InstanceId"/> — the cloner preserves this value across
/// clones (see <c>GameStateCloner</c>). If any InstanceId is missing from the
/// live eligible set (shouldn't happen for a legal move) the fallback heuristic
/// picks for safety.
/// </para>
///
/// <para>YAGNI — only combat decisions are searched in this phase. All other
/// prompts delegate to the inner heuristic. Priority search is Task D2.</para>
/// </summary>
internal sealed class SearchStrategy : IBotStrategy
{
    private readonly HeuristicStrategy _heuristic;
    private readonly Mcts _mcts;
    private readonly ArchetypeWeights _weights;
    private readonly bool _prioritySearchEnabled;
    private readonly IDeckStrategy? _deckStrategy;

    /// <summary>
    /// Map a <see cref="BotConfig"/> to a sensible <see cref="MctsConfig"/>.
    ///
    /// <para>
    /// <c>DepthTurns=1</c> is the Stage-A finding: deeper rollouts wash out
    /// because the heuristic strategy recovers most positions to a similar
    /// score, while shallower trees finish faster and converge more reliably
    /// within the iteration budget. MaxIterations is capped at 200 to keep
    /// combat decisions within the engine's synchronous call budget.
    /// </para>
    /// </summary>
    private static MctsConfig ConfigFrom(BotConfig bot) => new(
        MaxIterations: bot.MaxMctsIterations ?? 200,
        MaxMillis: bot.MaxMctsBudgetMs ?? 1500,
        DepthTurns: 1,
        ExplorationC: 1.41);

    /// <summary>Production constructor. Resolves the deck strategy from the
    /// registry (null when no strategy is registered for the archetype, which
    /// preserves byte-identical behaviour for all existing bots).</summary>
    public SearchStrategy(BotConfig config)
        : this(config, deckOverride: null) { }

    /// <summary>Internal test-seam constructor. Accepts an explicit
    /// <paramref name="deckOverride"/> so unit tests can inject stubs without
    /// a registered <see cref="IDeckStrategy"/> in the assembly.</summary>
    internal SearchStrategy(BotConfig config, IDeckStrategy? deckOverride)
    {
        ArgumentNullException.ThrowIfNull(config);
        // Resolve the per-deck strategic advisor.  deckOverride is the test-seam
        // path; in production we ask the registry (null → unchanged behavior).
        var deckStrategy = deckOverride ?? DeckStrategyRegistry.For(config.ArchetypeName);
        _deckStrategy = deckStrategy;
        _heuristic = new HeuristicStrategy(config, deckStrategy);
        // WeightsOverride: use explicit vector when provided; fall back to the
        // archetype lookup so default behavior is completely unchanged.
        _weights = config.WeightsOverride ?? ArchetypeWeights.ForArchetype(config.ArchetypeName);
        _prioritySearchEnabled = config.PrioritySearchEnabled;
        var sim = new EngineSimulator(_weights, deck: deckStrategy);
        _mcts = new Mcts(sim, ConfigFrom(config));
    }

    // ── Combat decisions — run the search ─────────────────────────────────────

    /// <inheritdoc/>
    public CombatPlan PickAttackers(GameContext ctx, Player self, IReadOnlyList<Creature> eligible)
    {
        if (eligible.Count == 0)
            return CombatPlan.None;

        // Capture root state. GameContext.CurrentPhase is a StepStateType (fine-grained
        // step), but SimState wants a PhaseStateType (coarse phase). The engine has
        // already entered combat; resume from the Combat phase so the sandbox picks up
        // the BeginningOfCombat window and then surfaces DeclareAttackers.
        var root = SimState.Capture(
            livePlayers: ctx.AllPlayers,
            activePlayer: ctx.ActivePlayer,
            turnNumber: ctx.TurnNumber,
            phase: PhaseStateType.Combat,
            searchedSeat: self);

        SimMove chosen;
        try
        {
            chosen = _mcts.Search(root);
        }
        catch
        {
            // Any search failure (e.g. terminal root) → fall back to heuristic.
            return _heuristic.PickAttackers(ctx, self, eligible);
        }

        // MCTS may return a Priority action (not a CombatPlan) when priority search
        // is enabled and the root decision surfaced as a priority window
        // (e.g. BeginningOfCombat) rather than DeclareAttackers. In that case
        // the search correctly chose the best priority action, but we still need
        // to decide on attackers — fall back to the heuristic for that decision
        // rather than returning CombatPlan.None (which silently skips the attack).
        if (chosen.CombatPlan == null)
            return _heuristic.PickAttackers(ctx, self, eligible);

        // Explicit empty-attack: search deliberately chose to not attack.
        if (chosen.IsEmptyAttack)
            return CombatPlan.None;

        // ── InstanceId remap ─────────────────────────────────────────────────
        // The chosen plan's Creature references are sandbox clones. Re-map them
        // to the LIVE eligible creatures by InstanceId so the engine accepts them.
        var liveById = eligible.ToDictionary(c => c.InstanceId);
        var remapped = new List<AttackerDeclaration>(chosen.CombatPlan.Attackers.Count);

        foreach (var decl in chosen.CombatPlan.Attackers)
        {
            if (!liveById.TryGetValue(decl.Attacker.InstanceId, out var liveAttacker))
            {
                // Stale InstanceId — remap failed. Fall back to heuristic for safety.
                return _heuristic.PickAttackers(ctx, self, eligible);
            }

            // Resolve the defending player: find the live player by Id in AllPlayers.
            object defender;
            if (decl.DefendingPlayerOrPlaneswalker is Player sandboxPlayer)
            {
                var liveDefender = ctx.AllPlayers.FirstOrDefault(p => p.Id == sandboxPlayer.Id);
                // preserves: liveAttacker, decl.DefendingPlayerOrPlaneswalker (identity mapped)
                defender = liveDefender ?? ctx.AllPlayers.First(p => !ReferenceEquals(p, self));
            }
            else
            {
                defender = ctx.AllPlayers.First(p => !ReferenceEquals(p, self));
            }

            remapped.Add(new AttackerDeclaration(liveAttacker, defender));
        }

        return remapped.Count == 0 ? CombatPlan.None : new CombatPlan(remapped);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Block decisions are evaluated via direct combat-outcome scoring over the
    /// enriched candidate set produced by <see cref="SearchAgent.BuildBlockerMoves"/>
    /// (accessible via <see cref="BlockCombatEval"/>), rather than full MCTS.
    ///
    /// Rationale — why PickBlockers does NOT use MCTS (and should not in Phase 2
    /// without combat-state resume support):
    ///
    /// The bot's block decision is made against the SPECIFIC live attack that was
    /// just declared by the real opponent. If we launched an MCTS sandbox to search
    /// the block decision, the sandbox would resume from a pre-combat game state and
    /// the sandbox's opponent agent (even the adversarial HeuristicStrategy added in
    /// Task D3) would re-derive its OWN attack from that state — which may differ
    /// from the real attack. The MCTS would then search blocks against the WRONG
    /// attack, producing a meaningless or misleading block plan.
    ///
    /// The correct fix (Phase 2) is "combat-state resume": clone the game state
    /// AFTER attackers are declared (mid-combat) so the sandbox sees the real attack.
    /// Until that infrastructure exists, <see cref="BlockCombatEval"/> — a direct
    /// one-ply lethal-aware combat projector over the actual attackers — is the
    /// correct and safe tool for this decision class.
    ///
    /// The evaluator is lethal-aware: a plan that lets all-attacker power through
    /// when it would kill the defender is scored as <c>double.MinValue</c> so
    /// chump blocks (even losing the blocker) beat taking lethal damage.
    /// </remarks>
    public BlockPlan PickBlockers(
        GameContext ctx,
        Player self,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligible)
    {
        if (attackers.Count == 0 || eligible.Count == 0)
            return BlockPlan.None;

        // Enumerate the enriched candidate set (includes chump, trade, gang blocks).
        var candidates = BlockCombatEval.EnumeratePlans(attackers, eligible);

        // Score each candidate and pick the best.
        var best = BlockCombatEval.PickBest(candidates, attackers, self.LifeTotal, _weights);
        return best;
    }

    // ── Priority decisions — MCTS-backed (Task D2) ────────────────────────────

    /// <summary>
    /// Selects a priority action using MCTS when there are real choices;
    /// short-circuits to Pass when only Pass is legal (avoids wasted search).
    ///
    /// <para>
    /// <b>InstanceId remap:</b> The MCTS search operates over sandbox clones.
    /// The chosen <see cref="SimMove"/> carries a <see cref="PriorityAction"/>
    /// that references cloned objects. This method remaps them back to the
    /// LIVE objects by InstanceId before returning.
    /// </para>
    ///
    /// <para>
    /// Supported with full remap: <c>PlayLand</c>, <c>Pass</c>.
    /// <c>CastSpell</c> and <c>ActivateAbility</c> fall back to the inner
    /// heuristic when found as the chosen action (Phase 1 scope — complex
    /// targeting / ability remaps deferred).
    /// </para>
    /// </summary>
    public PriorityAction PickPriorityAction(GameContext ctx, Player self)
    {
        // Directive override: if the deck strategy has identified an assembled
        // win-line and knows the next action, execute it immediately — before
        // any heuristic or MCTS search.  Re-evaluated each priority window.
        var win = _deckStrategy?.TryGetNextWinningAction(ctx, self);
        if (win is not null) return win;

        // Short-circuit: if priority MCTS is disabled, delegate to the inner heuristic.
        // Used in tests where sandbox games hit the priority-loop-safety limit,
        // making each MCTS call prohibitively slow (e.g., unimplemented Burn cards).
        if (!_prioritySearchEnabled)
            return _heuristic.PickPriorityAction(ctx, self);

        // Defensive guard: if the player has already lost, do not launch search.
        // The engine's PriorityLoop now skips lost players before reaching this
        // method (CR 800.4a fix), but this guard provides an additional safety
        // layer in case the strategy is called from another code path or tests.
        if (self.HasLost)
            return PriorityAction.Pass;

        // Step 1 — enumerate legal actions.
        var legal = LegalActionEnumerator.ForPriority(ctx, self);

        // Step 2 — short-circuit: if only Pass is legal (or empty), return Pass.
        if (legal.Count <= 1)
            return PriorityAction.Pass;

        // Step 3 — build SimState and run MCTS.
        // CurrentPhase is StepStateType (fine-grained step). Convert to
        // PhaseStateType (coarse phase) for SimState. Default to PreCombatMain.
        var phase = ctx.CurrentPhase?.ToPhaseStateType() ?? PhaseStateType.PreCombatMain;

        SimState root;
        try
        {
            root = SimState.Capture(
                livePlayers: ctx.AllPlayers,
                activePlayer: ctx.ActivePlayer,
                turnNumber: ctx.TurnNumber,
                phase: phase,
                searchedSeat: self);
        }
        catch
        {
            return _heuristic.PickPriorityAction(ctx, self);
        }

        SimMove chosen;
        try
        {
            chosen = _mcts.Search(root);
        }
        catch
        {
            // Any search failure → fall back to heuristic for correctness.
            return _heuristic.PickPriorityAction(ctx, self);
        }

        // Step 4 — map the chosen SimMove back to a LIVE PriorityAction by InstanceId.
        return RemapPriorityAction(chosen, ctx, self);
    }

    /// <summary>
    /// Remaps a <see cref="SimMove"/>'s <see cref="PriorityAction"/> from
    /// sandbox-cloned objects to the LIVE engine objects by InstanceId.
    /// Falls back to heuristic on any remap failure.
    /// </summary>
    private PriorityAction RemapPriorityAction(SimMove chosen, GameContext ctx, Player self)
    {
        var action = chosen.PriorityAction;
        if (action == null)
            return _heuristic.PickPriorityAction(ctx, self);

        return action switch
        {
            PriorityAction.PassAction =>
                // Pass needs no remap.
                PriorityAction.Pass,

            PriorityAction.PlayLand pl =>
                // Find the live land in self's hand by InstanceId.
                RemapPlayLand(pl, ctx, self),

            PriorityAction.CastSpell cs =>
                // Remap the sandbox card to the LIVE card by InstanceId.
                // LegalActionEnumerator always creates CastSpell with empty
                // targets (Array.Empty<object>()); the PriorityLoop's cast
                // dispatcher (TurnDriver.DispatchCast) prompts the agent for
                // targets at cast time via ChooseTargetsAsync — so we do not
                // need to remap targets here. For safety, if targets are
                // non-empty (future extension) we fall through to heuristic
                // for any target that cannot be remapped.
                RemapCastSpell(cs, ctx, self),

            PriorityAction.ActivateAbility =>
                // ActivateAbility remap deferred to Phase 2 (ability lookup
                // by source InstanceId is non-trivial).
                // Fall back to heuristic for correctness.
                _heuristic.PickPriorityAction(ctx, self),

            _ =>
                // Unknown/unexpected action kind → heuristic fallback.
                _heuristic.PickPriorityAction(ctx, self),
        };
    }

    /// <summary>
    /// Remaps a <see cref="PriorityAction.CastSpell"/> from the sandbox clone
    /// to the live card in <paramref name="self"/>'s hand by InstanceId.
    ///
    /// <para>
    /// <b>Target remap:</b> <see cref="LegalActionEnumerator.ForPriority"/> always
    /// emits <c>CastSpell</c> with <c>Array.Empty&lt;object&gt;()</c> targets.
    /// The live engine's cast dispatcher (<c>TurnDriver.DispatchCast</c>) then
    /// calls <see cref="IPlayerAgent.ChooseTargetsAsync"/> to gather real targets
    /// at cast time, delegated to <see cref="SearchStrategy.PickTargets"/> (which
    /// calls the inner heuristic's <see cref="Heuristic.TargetPolicy"/>). So we
    /// pass through the empty target list as-is — target selection happens
    /// naturally post-cast, not here.
    /// </para>
    ///
    /// <para>
    /// If targets were non-empty (possible in a future extension where the
    /// enumerator pre-selects targets), we attempt a best-effort remap:
    ///   • <see cref="Majik.Core.Cards.Permanent"/> / <see cref="ICard"/>: match
    ///     by <c>InstanceId</c> across all live zones (hand + battlefield of both players).
    ///   • <see cref="Player"/>: match by <c>Player.Id</c>.
    ///   • Unknown type: fall back to heuristic for safety.
    /// </para>
    ///
    /// <para>Falls back to heuristic if the live card is not found in hand.</para>
    /// </summary>
    private PriorityAction RemapCastSpell(
        PriorityAction.CastSpell sandboxAction,
        GameContext ctx,
        Player self)
    {
        // Find the live card in self's hand by InstanceId.
        var liveCard = self.Zones.Hand.GetCards()
            .FirstOrDefault(c => c.InstanceId == sandboxAction.Card.InstanceId);

        if (liveCard == null)
        {
            // InstanceId not found — sandbox state diverged or card left hand.
            // Fall back to heuristic for correctness.
            return _heuristic.PickPriorityAction(ctx, self);
        }

        // Fast path: no targets to remap (the common case — LegalActionEnumerator
        // always creates CastSpell with empty targets).
        if (sandboxAction.Targets.Count == 0)
        {
            // preserves: liveCard (remapped by InstanceId), HoldPriority, AlternativeCost, AdditionalCosts
            return new PriorityAction.CastSpell(
                liveCard,
                Array.Empty<object>(),
                sandboxAction.HoldPriority,
                sandboxAction.AlternativeCost,
                sandboxAction.AdditionalCosts);
        }

        // Non-empty targets: attempt best-effort remap by InstanceId / Player.Id.
        // Build lookup maps for live objects across all zones of all players.
        var liveCardsById = ctx.AllPlayers
            .SelectMany(p =>
                p.Zones.Hand.GetCards()
                    .Concat(p.Zones.Battlefield.GetCards())
                    .Concat(p.Zones.Graveyard.GetCards()))
            .GroupBy(c => c.InstanceId)
            .ToDictionary(g => g.Key, g => g.First());

        var livePlayersById = ctx.AllPlayers.ToDictionary(p => p.Id);

        var remappedTargets = new List<object>(sandboxAction.Targets.Count);
        foreach (var target in sandboxAction.Targets)
        {
            switch (target)
            {
                case ICard targetCard:
                    if (liveCardsById.TryGetValue(targetCard.InstanceId, out var liveTarget))
                        remappedTargets.Add(liveTarget);
                    else
                        // Target not found in live zones — fall back to heuristic.
                        return _heuristic.PickPriorityAction(ctx, self);
                    break;
                case Player targetPlayer:
                    if (livePlayersById.TryGetValue(targetPlayer.Id, out var livePlayer))
                        remappedTargets.Add(livePlayer);
                    else
                        return _heuristic.PickPriorityAction(ctx, self);
                    break;
                default:
                    // Unknown target type — can't remap safely. Fall back to heuristic.
                    return _heuristic.PickPriorityAction(ctx, self);
            }
        }

        // preserves: liveCard, remappedTargets, HoldPriority, AlternativeCost, AdditionalCosts
        return new PriorityAction.CastSpell(
            liveCard,
            remappedTargets,
            sandboxAction.HoldPriority,
            sandboxAction.AlternativeCost,
            sandboxAction.AdditionalCosts);
    }

    /// <summary>
    /// Remaps a <see cref="PriorityAction.PlayLand"/> from the sandbox clone
    /// to the live land in self's hand by InstanceId. Falls back to heuristic
    /// if the land is not found or if the live context does not permit a land play.
    ///
    /// <para>
    /// The live-context check (<c>ctx.LandPlayAvailable</c>) is critical: the MCTS
    /// sandbox may generate PlayLand actions in a sandbox turn where the searched
    /// player IS active and in a main phase, even if the LIVE game is in a priority
    /// window where a land play is illegal (e.g. opponent's turn, combat step, or
    /// the land drop has already been used this live turn). Without this guard, the
    /// MCTS returns PlayLand and the live PriorityLoop rejects it ~50k times per
    /// 20-game run — a spin loop that saturates the priority action cap and forces
    /// nearly every game to a draw by turn cap.
    /// </para>
    /// </summary>
    private PriorityAction RemapPlayLand(
        PriorityAction.PlayLand sandboxAction,
        GameContext ctx,
        Player self)
    {
        // Guard: live context must allow a land play. If not, the MCTS suggested
        // a land play that is valid in the SANDBOX turn but invalid in the LIVE
        // game (wrong phase, opponent's turn, or drop already used). Fall back to
        // heuristic so the live engine doesn't reject and re-prompt endlessly.
        if (!ctx.LandPlayAvailable)
            return _heuristic.PickPriorityAction(ctx, self);

        var liveLand = self.Zones.Hand.GetCards()
            .FirstOrDefault(c => c.InstanceId == sandboxAction.Land.InstanceId);

        if (liveLand == null)
        {
            // InstanceId not found — sandbox state diverged. Fall back to heuristic.
            return _heuristic.PickPriorityAction(ctx, self);
        }

        // preserves: liveLand (remapped), HoldPriority (from sandbox action)
        return new PriorityAction.PlayLand(liveLand, HoldPriority: sandboxAction.HoldPriority);
    }

    // ── All other decisions — delegate to heuristic ────────────────────────────

    public MulliganDecision PickMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
        => _heuristic.PickMulligan(hand, mulligansTaken);

    public IReadOnlyList<ICard> PickCardsToBottom(IReadOnlyList<ICard> hand, int countToBottom)
        => _heuristic.PickCardsToBottom(hand, countToBottom);

    public IReadOnlyList<object> PickTargets(GameContext ctx, Player self, TargetRequest request)
        => _heuristic.PickTargets(ctx, self, request);

    public int PickX(GameContext ctx, Player self)
        => _heuristic.PickX(ctx, self);

    public int PickMode(GameContext ctx, Player self, IReadOnlyList<string> modes)
        => _heuristic.PickMode(ctx, self, modes);

    public ManaPayment PickMana(GameContext ctx, Player self, ManaCost cost)
        => _heuristic.PickMana(ctx, self, cost);

    public IReadOnlyList<ITriggeredAbility> OrderTriggers(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine)
        => _heuristic.OrderTriggers(ctx, mine);

    public Majik.Core.Keywords.ScryAction.ScryDecision PickScry(
        GameContext? ctx, Player self, IReadOnlyList<ICard> peeked)
        => _heuristic.PickScry(ctx, self, peeked);

    public Majik.Core.Keywords.SurveilAction.SurveilDecision PickSurveil(
        GameContext? ctx, Player self, IReadOnlyList<ICard> peeked)
        => _heuristic.PickSurveil(ctx, self, peeked);

    public ICard? PickLibraryCard(GameContext? ctx, Player self, IReadOnlyList<ICard> candidates, string kindLabel)
        => _heuristic.PickLibraryCard(ctx, self, candidates, kindLabel);
}
