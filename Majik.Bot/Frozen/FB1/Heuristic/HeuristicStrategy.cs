using Majik.Bot.Diagnostics;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Frozen.FB1;

/// <summary>
/// FB1 — the FROZEN baseline heuristic (`BotConfig.Strategy = "frozen-fb1"`).
/// Byte-identical vendored snapshot of <see cref="Majik.Bot.Heuristic.HeuristicStrategy"/>
/// and its full policy/eval/combat closure, cut 2026-06-12 at commit
/// 38547ffb3. The permanent reference opponent for the strength ladder:
/// its POLICY must never change. Maintenance contract: mechanical,
/// behavior-preserving patches for engine API renames ONLY — any behavioral
/// edit trips <c>FB1CharacterizationTests</c> and must be reverted.
/// </summary>
internal sealed class HeuristicStrategy : IBotStrategy
{
    private readonly ArchetypeWeights _weights;
    private readonly PriorityPolicy _priority;
    private readonly CombatPolicy _combat;
    private readonly IBotDecisionSink _sink;

    private readonly Majik.Core.Diagnostics.VanillaShellTracker? _vanillaTracker;

    public HeuristicStrategy(BotConfig config)
    {
        // WeightsOverride: use explicit vector when provided; fall back to the
        // archetype lookup so default behavior is completely unchanged.
        // The override arrives as the LIVE Majik.Bot.Evaluation.ArchetypeWeights
        // record (BotConfig is plumbing, not frozen) and is mapped field-by-field
        // into the FROZEN record — named args, preserves: LifeDelta, BoardPower,
        // BoardToughness, OpponentThreats, ManaSources, HandSize, Tempo,
        // KeyCardInPlay, LethalProximity, CardAdvantage, PlaneswalkerEngine,
        // HiddenReach.
        _weights = config.WeightsOverride is { } w
            ? new ArchetypeWeights(
                LifeDelta:          w.LifeDelta,
                BoardPower:         w.BoardPower,
                BoardToughness:     w.BoardToughness,
                OpponentThreats:    w.OpponentThreats,
                ManaSources:        w.ManaSources,
                HandSize:           w.HandSize,
                Tempo:              w.Tempo,
                KeyCardInPlay:      w.KeyCardInPlay,
                LethalProximity:    w.LethalProximity,
                CardAdvantage:      w.CardAdvantage,
                PlaneswalkerEngine: w.PlaneswalkerEngine,
                HiddenReach:        w.HiddenReach)
            : ArchetypeWeights.ForArchetype(config.ArchetypeName);
        _sink = config.DecisionSink ?? NullBotDecisionSink.Instance;
        _vanillaTracker = config.VanillaShellTracker;
        _priority = new PriorityPolicy(_weights, _sink, _vanillaTracker);
        // SimCombatBudgetMs caps the CombatPolicy stopwatch budget for sandbox
        // opponent agents inside MCTS. The production default (~800 ms) is used
        // when the field is null (live agent or test without an explicit cap).
        var combatBudgetMs = config.SimCombatBudgetMs ?? 800;
        _combat = new CombatPolicy(_weights, budgetMs: combatBudgetMs, sink: _sink);
    }

    public PriorityAction PickPriorityAction(GameContext ctx, Player self) => _priority.Pick(ctx, self);

    public MulliganDecision PickMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
        => MulliganPolicy.Decide(hand, mulligansTaken);

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
