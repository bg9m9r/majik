using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// PLAN 01 (Slice C) — the legacy bespoke <c>ChooseXxxAsync</c> methods are now
/// default-implemented shims that build a <see cref="ChoiceRequest"/> and route
/// through the single <see cref="IPlayerAgent.ChooseAsync"/> sink. These tests
/// pin (a) that each shim invokes <c>ChooseAsync</c> with the right
/// <see cref="ChoiceKind"/> / candidates, and (b) that the default
/// <c>ChooseAsync</c> preserves the historical <c>candidates[0]</c> / decline
/// defaults.
/// </summary>
public class ChooseAsyncShimTests
{
    /// <summary>
    /// Minimal agent that leaves every bespoke prompt as its interface-default
    /// shim and only records the <see cref="ChoiceRequest"/> handed to
    /// <see cref="ChooseAsync"/> (returning a scripted result). The required
    /// (non-default) interface members throw — they're never exercised here.
    /// </summary>
    private sealed class RecordingChooseAgent : IPlayerAgent
    {
        public ChoiceRequest? LastRequest { get; private set; }
        public IReadOnlyList<object> NextResult { get; set; } = System.Array.Empty<object>();

        public Task<IReadOnlyList<object>> ChooseAsync(GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        {
            LastRequest = req;
            return Task.FromResult(NextResult);
        }

        // Required (non-default) members — unused by these shim tests.
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new System.NotSupportedException();
    }

    private static Card Card(string name) => new(name, "");

    [Fact]
    public async Task LibraryPickShim_RoutesThroughChooseAsync_AsPickOne()
    {
        var c0 = Card("a");
        var c1 = Card("b");
        var agent = new RecordingChooseAgent { NextResult = new object[] { c1 } };

        var picked = await ((IPlayerAgent)agent).ChooseLibraryPickAsync(null, new[] { c0, c1 }, "creature card");

        agent.LastRequest!.Kind.Should().Be(ChoiceKind.PickOne);
        agent.LastRequest.Candidates.Should().Equal(c0, c1);
        picked.Should().BeSameAs(c1);
    }

    [Fact]
    public async Task FromHandShim_RoutesThroughChooseAsync_AsPickOne_WithIntent()
    {
        var c0 = Card("a");
        var agent = new RecordingChooseAgent { NextResult = new object[] { c0 } };
        var player = new Player("P", 20);

        var picked = await ((IPlayerAgent)agent).ChooseFromHandAsync(player, new[] { c0 }, BotIntent.Discard);

        agent.LastRequest!.Kind.Should().Be(ChoiceKind.PickOne);
        agent.LastRequest.Intent.Should().Be(BotIntent.Discard);
        picked.Should().BeSameAs(c0);
    }

    [Fact]
    public async Task GiftRecipientShim_RoutesThroughChooseAsync_AsOptionalPickOne()
    {
        var agent = new RecordingChooseAgent { NextResult = System.Array.Empty<object>() };
        var opp = new Player("Opp", 20);

        var picked = await ((IPlayerAgent)agent).ChooseGiftRecipientAsync(
            null!, Card("gift source"), "a 1/1 Fish", new[] { opp });

        agent.LastRequest!.Kind.Should().Be(ChoiceKind.PickOne);
        agent.LastRequest.Optional.Should().BeTrue();
        picked.Should().BeNull(); // declined (empty result)
    }

    [Fact]
    public async Task YesNoShim_RoutesThroughChooseAsync_AsYesNo()
    {
        var agentYes = new RecordingChooseAgent { NextResult = new object[] { true } };
        (await ((IPlayerAgent)agentYes).ChooseYesNoAsync(null, "Pay 2 life?", "Shock")).Should().BeTrue();
        agentYes.LastRequest!.Kind.Should().Be(ChoiceKind.YesNo);

        var agentNo = new RecordingChooseAgent { NextResult = System.Array.Empty<object>() };
        (await ((IPlayerAgent)agentNo).ChooseYesNoAsync(null, "Pay 2 life?", "Shock")).Should().BeFalse();
    }

    [Fact]
    public async Task FromRevealedShim_RoutesOverEligibleSubset()
    {
        var elig0 = Card("eligible");
        var agent = new RecordingChooseAgent { NextResult = new object[] { elig0 } };

        var picked = await ((IPlayerAgent)agent).ChooseFromRevealedAsync(
            null, revealed: new[] { Card("r1"), elig0 }, eligible: new[] { elig0 },
            optional: false, label: "Permanent to hand");

        agent.LastRequest!.Candidates.Should().Equal(elig0);
        picked.Should().BeSameAs(elig0);
    }

    // ----- default ChooseAsync preserves the historical defaults -----

    /// <summary>Agent that overrides nothing — exercises the interface-default
    /// <see cref="IPlayerAgent.ChooseAsync"/> directly.</summary>
    private sealed class DefaultAgent : IPlayerAgent
    {
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new System.NotSupportedException();
    }

    [Fact]
    public async Task DefaultChooseAsync_PickOne_ReturnsFirstCandidate()
    {
        var c0 = Card("a");
        var picked = await ((IPlayerAgent)new DefaultAgent()).ChooseLibraryPickAsync(
            null, new[] { c0, Card("b") }, "creature");

        picked.Should().BeSameAs(c0);
    }

    [Fact]
    public async Task DefaultChooseAsync_PickOne_EmptyCandidates_ReturnsNull()
    {
        var picked = await ((IPlayerAgent)new DefaultAgent()).ChooseLibraryPickAsync(
            null, System.Array.Empty<ICard>(), "creature");

        picked.Should().BeNull();
    }

    [Fact]
    public async Task DefaultChooseAsync_OptionalGift_Declines()
    {
        var opp = new Player("Opp", 20);
        var picked = await ((IPlayerAgent)new DefaultAgent()).ChooseGiftRecipientAsync(
            null!, Card("src"), "gift", new[] { opp });

        picked.Should().BeNull();
    }

    [Fact]
    public async Task DefaultChooseAsync_YesNo_UpsideIntent_ReturnsYes()
    {
        var req = new ChoiceRequest(ChoiceKind.YesNo, "Draw a card?", 0, 1,
            System.Array.Empty<object>(), Intent: BotIntent.CardAdvantage, Optional: true);

        var chosen = await ((IPlayerAgent)new DefaultAgent()).ChooseAsync(null!, req);

        chosen.Should().NotBeEmpty(); // "yes"
    }

    [Fact]
    public async Task DefaultChooseAsync_YesNo_DownsideIntent_ReturnsNo()
    {
        var req = new ChoiceRequest(ChoiceKind.YesNo, "Pay 2 life?", 0, 1,
            System.Array.Empty<object>(), Intent: BotIntent.LoseLife, Optional: true);

        var chosen = await ((IPlayerAgent)new DefaultAgent()).ChooseAsync(null!, req);

        chosen.Should().BeEmpty(); // "no"
    }
}
