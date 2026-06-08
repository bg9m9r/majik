using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
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
        MaxIterations: 200,
        MaxMillis: 1500,
        DepthTurns: 1,
        ExplorationC: 1.41);

    public SearchStrategy(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _heuristic = new HeuristicStrategy(config);
        _weights = ArchetypeWeights.ForArchetype(config.ArchetypeName);
        var sim = new EngineSimulator(_weights);
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

        // Empty-attack: pass combat.
        if (chosen.CombatPlan == null || chosen.IsEmptyAttack)
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
    /// Rationale: MCTS block search would require the sandbox's opponent agent
    /// (DeterministicBotAgent) to actually declare attackers so the engine reaches
    /// a DeclareBlockers node — but DeterministicBotAgent always passes, so the
    /// sandbox never surfaces that decision. A direct combat-projection evaluator
    /// over the enumerated candidates is correct, fast, and deterministic.
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

    // ── All other decisions — delegate to heuristic ────────────────────────────

    public PriorityAction PickPriorityAction(GameContext ctx, Player self)
        => _heuristic.PickPriorityAction(ctx, self);

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
