using Majik.Bot.Heuristic;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Bot;

/// <summary>
/// IPlayerAgent implementation that dispatches every prompt through an
/// IBotStrategy. v1 ships HeuristicStrategy chosen via BotConfig.Strategy.
/// </summary>
public sealed class BotPlayerAgent : IPlayerAgent
{
    private readonly Player _self;
    private readonly IBotStrategy _strategy;

    public BotPlayerAgent(Player self, BotConfig config)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _strategy = config.Strategy switch
        {
            "heuristic" => new HeuristicStrategy(config),
            "mcts"      => throw new NotImplementedException("MCTS strategy reserved for v2."),
            _ => throw new ArgumentException($"Unknown strategy: {config.Strategy}", nameof(config)),
        };
    }

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickPriorityAction(ctx, _self)); }

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickMulligan(hand, mulligansTaken)); }

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickCardsToBottom(hand, countToBottom)); }

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickTargets(ctx, _self, request)); }

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickX(ctx, _self)); }

    public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickMode(ctx, _self, modes)); }

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.OrderTriggers(ctx, mine)); }

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickMana(ctx, _self, cost)); }

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickAttackers(ctx, _self, eligibleAttackers)); }

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickBlockers(ctx, _self, attackers, eligibleBlockers)); }

    public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickScry(ctx, _self, peeked)); }

    public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(_strategy.PickSurveil(ctx, _self, peeked)); }
}
