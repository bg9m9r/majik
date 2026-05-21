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
    private readonly Action<bool>? _onThinking;

    public BotPlayerAgent(Player self, BotConfig config, Action<bool>? onThinking = null)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _onThinking = onThinking;
        _strategy = config.Strategy switch
        {
            "heuristic" => new HeuristicStrategy(config),
            "mcts"      => throw new NotImplementedException("MCTS strategy reserved for v2."),
            _ => throw new ArgumentException($"Unknown strategy: {config.Strategy}", nameof(config)),
        };
    }

    /// <summary>
    /// Wraps a synchronous policy call with the optional thinking callback.
    /// Fires <c>onThinking(true)</c> before, <c>onThinking(false)</c> after.
    /// Observer exceptions are swallowed so a faulty subscriber cannot abort
    /// the engine.
    /// </summary>
    private Task<T> WrapAsync<T>(Func<T> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { _onThinking?.Invoke(true); }
        catch { /* observer fault must not abort engine */ }
        try
        {
            return Task.FromResult(work());
        }
        finally
        {
            try { _onThinking?.Invoke(false); }
            catch { /* observer fault must not abort engine */ }
        }
    }

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickPriorityAction(ctx, _self), ct);

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMulligan(hand, mulligansTaken), ct);

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickCardsToBottom(hand, countToBottom), ct);

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickTargets(ctx, _self, request), ct);

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickX(ctx, _self), ct);

    public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMode(ctx, _self, modes), ct);

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => WrapAsync(() => _strategy.OrderTriggers(ctx, mine), ct);

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickMana(ctx, _self, cost), ct);

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickAttackers(ctx, _self, eligibleAttackers), ct);

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickBlockers(ctx, _self, attackers, eligibleBlockers), ct);

    public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickScry(ctx, _self, peeked), ct);

    public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => WrapAsync(() => _strategy.PickSurveil(ctx, _self, peeked), ct);
}
