using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Bot;

/// <summary>
/// Strategy abstraction so a future MCTS implementation can plug in
/// alongside the v1 heuristic without touching BotPlayerAgent.
/// </summary>
internal interface IBotStrategy
{
    PriorityAction PickPriorityAction(GameContext ctx, Player self);
    MulliganDecision PickMulligan(IReadOnlyList<ICard> hand, int mulligansTaken);
    IReadOnlyList<ICard> PickCardsToBottom(IReadOnlyList<ICard> hand, int countToBottom);
    IReadOnlyList<object> PickTargets(GameContext ctx, Player self, TargetRequest request);
    int PickX(GameContext ctx, Player self);
    int PickMode(GameContext ctx, Player self, IReadOnlyList<string> modes);
    ManaPayment PickMana(GameContext ctx, Player self, ManaCost cost);
    CombatPlan PickAttackers(GameContext ctx, Player self, IReadOnlyList<Creature> eligible);
    BlockPlan PickBlockers(GameContext ctx, Player self, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligible);
    IReadOnlyList<ITriggeredAbility> OrderTriggers(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine);
    Majik.Core.Keywords.ScryAction.ScryDecision PickScry(GameContext? ctx, Player self, IReadOnlyList<ICard> peeked);
    Majik.Core.Keywords.SurveilAction.SurveilDecision PickSurveil(GameContext? ctx, Player self, IReadOnlyList<ICard> peeked);
}
