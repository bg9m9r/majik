using Majik.Bot.Combat;
using Majik.Bot.Diagnostics;
using Majik.Bot.Evaluation;
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

    public HeuristicStrategy(BotConfig config)
    {
        _weights = ArchetypeWeights.ForArchetype(config.ArchetypeName);
        _sink = config.DecisionSink ?? NullBotDecisionSink.Instance;
        _priority = new PriorityPolicy(_weights, _sink);
        _combat = new CombatPolicy(_weights, sink: _sink);
    }

    public PriorityAction PickPriorityAction(GameContext ctx, Player self) => _priority.Pick(ctx, self);

    public MulliganDecision PickMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
        => MulliganPolicy.Decide(hand, mulligansTaken);

    public IReadOnlyList<ICard> PickCardsToBottom(IReadOnlyList<ICard> hand, int countToBottom)
        => hand.OrderByDescending(c => c is Land ? 0 : 1).Take(countToBottom).ToList();

    public IReadOnlyList<object> PickTargets(GameContext ctx, Player self, TargetRequest request)
        => TargetPolicy.Pick(ctx, self, request, _sink);

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
