using Majik.Bot.Combat;
using Majik.Bot.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Bot.Strategies;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Heuristic;

internal sealed class HeuristicStrategy : IBotStrategy
{
    private readonly ArchetypeWeights _weights;
    private readonly PriorityPolicy _priority;
    private readonly CombatPolicy _combat;
    private readonly IBotDecisionSink _sink;
    private readonly IDeckStrategy? _deckStrategy;

    private readonly Majik.Core.Diagnostics.VanillaShellTracker? _vanillaTracker;

    /// <summary>Production constructor. Resolves the deck strategy from the
    /// registry (null when no strategy is registered for the archetype, which
    /// preserves byte-identical behaviour for all existing bots).</summary>
    public HeuristicStrategy(BotConfig config)
        : this(config, deckOverride: null) { }

    /// <summary>Internal test-seam constructor. Accepts an explicit
    /// <paramref name="deckOverride"/> so unit tests can inject stubs without
    /// a registered <see cref="IDeckStrategy"/> in the assembly.</summary>
    internal HeuristicStrategy(BotConfig config, IDeckStrategy? deckOverride)
    {
        // WeightsOverride: use explicit vector when provided; fall back to the
        // archetype lookup so default behavior is completely unchanged.
        _weights = config.WeightsOverride ?? ArchetypeWeights.ForArchetype(config.ArchetypeName);
        _sink = config.DecisionSink ?? NullBotDecisionSink.Instance;
        _vanillaTracker = config.VanillaShellTracker;
        // Resolve the per-deck strategic advisor.  deckOverride is the test-seam
        // path; in production we ask the registry (null → unchanged behavior).
        _deckStrategy = deckOverride ?? DeckStrategyRegistry.For(config.ArchetypeName);
        _priority = new PriorityPolicy(_weights, _sink, _vanillaTracker, _deckStrategy);
        // SimCombatBudgetMs caps the CombatPolicy stopwatch budget for sandbox
        // opponent agents inside MCTS. The production default (~800 ms) is used
        // when the field is null (live agent or test without an explicit cap).
        var combatBudgetMs = config.SimCombatBudgetMs ?? 800;
        _combat = new CombatPolicy(_weights, budgetMs: combatBudgetMs, sink: _sink);
    }

    public PriorityAction PickPriorityAction(GameContext ctx, Player self)
    {
        // Directive override: if the deck strategy has identified an assembled
        // win-line and knows the next action, execute it immediately before any
        // heuristic/search scoring.  Re-evaluated each priority window so it
        // stays current with the board state.
        var win = _deckStrategy?.TryGetNextWinningAction(ctx, self);
        if (win is not null) return win;

        return _priority.Pick(ctx, self);
    }

    public MulliganDecision PickMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
        => _deckStrategy?.AdviseMulligan(hand, mulligansTaken) ?? MulliganPolicy.Decide(hand, mulligansTaken);

    public IReadOnlyList<ICard> PickCardsToBottom(IReadOnlyList<ICard> hand, int countToBottom)
        => hand.OrderByDescending(c => c is Land ? 0 : 1).Take(countToBottom).ToList();

    public IReadOnlyList<object> PickTargets(GameContext ctx, Player self, TargetRequest request)
    {
        // Vanilla-shell graceful degrade: a candidate that's a vanilla shell
        // still has its name + type + P/T (for creatures) — TargetPolicy.Score
        // happily ranks it. Notice the encounter so the operator knows the
        // bot's targeting EV is approximate for this card.
        if (_vanillaTracker is not null)
        {
            foreach (var candidate in request.LegalCandidates)
            {
                if (candidate is ICard c && c.IsVanillaShell)
                {
                    _vanillaTracker.Notice(c, self, "target candidate");
                }
            }
        }
        return TargetPolicy.Pick(ctx, self, request, _sink);
    }

    public int PickX(GameContext ctx, Player self) => ModalPolicy.PickX(ctx, self);

    public int PickMode(GameContext ctx, Player self, IReadOnlyList<string> modes)
        => ModalPolicy.PickMode(ctx, self, modes, _sink);

    public ManaPayment PickMana(GameContext ctx, Player self, ManaCost cost)
        => ManaPolicy.Pick(ctx, self, 0, 0);

    public CombatPlan PickAttackers(GameContext ctx, Player self, IReadOnlyList<Creature> eligible)
        => _combat.PickAttackers(ctx, self, eligible);

    public BlockPlan PickBlockers(GameContext ctx, Player self, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligible)
        => _combat.PickBlockers(ctx, self, attackers, eligible);

    public IReadOnlyList<ITriggeredAbility> OrderTriggers(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine)
        => TriggerOrderPolicy.Order(ctx, mine, _sink);

    public Majik.Core.Keywords.ScryAction.ScryDecision PickScry(GameContext? ctx, Player self, IReadOnlyList<ICard> peeked)
        => ctx == null
            ? new Majik.Core.Keywords.ScryAction.ScryDecision(Array.Empty<ICard>(), peeked.ToList())
            : ScrySurveilPolicy.Scry(ctx, self, peeked);

    public Majik.Core.Keywords.SurveilAction.SurveilDecision PickSurveil(GameContext? ctx, Player self, IReadOnlyList<ICard> peeked)
        => ctx == null
            ? new Majik.Core.Keywords.SurveilAction.SurveilDecision(Array.Empty<ICard>(), peeked.ToList())
            : ScrySurveilPolicy.Surveil(ctx, self, peeked);

    public ICard? PickLibraryCard(GameContext? ctx, Player self, IReadOnlyList<ICard> candidates, string kindLabel)
        => LibraryPickPolicy.Pick(self, candidates, kindLabel, _weights, ctx);
}
