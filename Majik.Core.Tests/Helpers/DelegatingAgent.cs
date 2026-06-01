using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Tests.Helpers;

/// <summary>
/// Test-only <see cref="IPlayerAgent"/> base that implements every abstract
/// interface member as a <c>virtual</c> method which throws by default. Tests
/// subclass it and override ONLY the one prompt they care about — any other
/// prompt firing surfaces loudly as a NotSupportedException rather than a
/// silent default. Used to prove a specific resolve path routes a choice
/// through a real (non-null) GameContext on the PLAN 01 async path.
/// </summary>
public abstract class DelegatingAgent : IPlayerAgent
{
    private static Task<T> Unexpected<T>(string method)
        => throw new NotSupportedException(
            $"DelegatingAgent: unexpected prompt {method} — the test under proof " +
            "should not reach this surface.");

    public virtual Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => Task.FromResult(PriorityAction.Pass);

    public virtual Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => Task.FromResult(MulliganDecision.Keep);

    public virtual Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => Unexpected<IReadOnlyList<ICard>>(nameof(ChooseCardsToBottomAsync));

    public virtual Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => Unexpected<IReadOnlyList<object>>(nameof(ChooseTargetsAsync));

    public virtual Task<IReadOnlyList<object>> ChooseAsync(GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        => Unexpected<IReadOnlyList<object>>(nameof(ChooseAsync));

    public virtual Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => Unexpected<int>(nameof(ChooseXAsync));

    public virtual Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
        => Unexpected<int>(nameof(ChooseModeAsync));

    public virtual Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => Task.FromResult(mine);

    public virtual Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => Unexpected<ManaPayment>(nameof(ChooseManaSourcesAsync));

    public virtual Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => Task.FromResult(CombatPlan.None);

    public virtual Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        => Task.FromResult(BlockPlan.None);

    public virtual Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => Unexpected<ScryAction.ScryDecision>(nameof(ChooseScryDecisionAsync));

    public virtual Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => Unexpected<SurveilAction.SurveilDecision>(nameof(ChooseSurveilDecisionAsync));
}
