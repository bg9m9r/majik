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
/// Pay-down of the <c>choose-and-order-n-hand-prompt</c> deferral. Adds the
/// <see cref="ChoiceKind.OrderedPickN"/> declarative choice (a JOINT "choose N
/// cards AND their order" decision in a single agent call) and the
/// <see cref="IPlayerAgent.ChooseAndOrderFromHandAsync"/> shim built on it.
///
/// <para>
/// Result contract: the returned list is the chosen cards in the order the
/// caller asked for, where index 0 is the FIRST element of the ordered result.
/// For a "put N cards on top of your library in any order" effect (Brainstorm,
/// CR 701.x library-top reorder) the caller treats result[0] as the card that
/// ends up ON TOP of the library.
/// </para>
/// </summary>
public class OrderedPickNTests
{
    private static Card Card(string name) => new(name, "");

    /// <summary>Agent that records the request + returns a scripted ordered
    /// result; leaves every other prompt as its interface-default shim.</summary>
    private sealed class RecordingChooseAgent : IPlayerAgent
    {
        public ChoiceRequest? LastRequest { get; private set; }
        public IReadOnlyList<object> NextResult { get; set; } = System.Array.Empty<object>();

        public Task<IReadOnlyList<object>> ChooseAsync(GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        {
            LastRequest = req;
            return Task.FromResult(NextResult);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new System.NotSupportedException();
    }

    [Fact]
    public async Task ChooseAndOrderShim_RoutesThroughChooseAsync_AsOrderedPickN()
    {
        var c0 = Card("a");
        var c1 = Card("b");
        var c2 = Card("c");
        // Agent returns c2 then c0 (joint pick + order).
        var agent = new RecordingChooseAgent { NextResult = new object[] { c2, c0 } };
        var player = new Player("P", 20);

        var ordered = await ((IPlayerAgent)agent).ChooseAndOrderFromHandAsync(
            player, new[] { c0, c1, c2 }, count: 2, BotIntent.LibraryReorder);

        agent.LastRequest!.Kind.Should().Be(ChoiceKind.OrderedPickN);
        agent.LastRequest.Min.Should().Be(2);
        agent.LastRequest.Max.Should().Be(2);
        agent.LastRequest.Intent.Should().Be(BotIntent.LibraryReorder);
        agent.LastRequest.Candidates.Should().Equal(c0, c1, c2);
        // Order preserved exactly as the agent returned it.
        ordered.Should().Equal(c2, c0);
    }

    /// <summary>Interface-default agent: an OrderedPickN with no smart policy
    /// returns the first <c>count</c> candidates in candidate order
    /// (deterministic pre-agent posture, matching every other prompt).</summary>
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
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new System.NotSupportedException();
    }

    [Fact]
    public async Task DefaultChooseAsync_OrderedPickN_ReturnsFirstCountInOrder()
    {
        var c0 = Card("a");
        var c1 = Card("b");
        var c2 = Card("c");
        var player = new Player("P", 20);

        var ordered = await ((IPlayerAgent)new DefaultAgent()).ChooseAndOrderFromHandAsync(
            player, new[] { c0, c1, c2 }, count: 2, BotIntent.LibraryReorder);

        ordered.Should().Equal(c0, c1);
    }

    [Fact]
    public async Task DefaultChooseAsync_OrderedPickN_FewerCandidatesThanCount_ReturnsAll()
    {
        var c0 = Card("a");
        var player = new Player("P", 20);

        var ordered = await ((IPlayerAgent)new DefaultAgent()).ChooseAndOrderFromHandAsync(
            player, new[] { c0 }, count: 2, BotIntent.LibraryReorder);

        ordered.Should().Equal(c0);
    }

    [Fact]
    public async Task ScriptedAgent_OrderedPickN_HonorsQueuedSelector()
    {
        var c0 = Card("a");
        var c1 = Card("b");
        var c2 = Card("c");
        var agent = new ScriptedAgent();
        // Queue a selector that returns c1 then c2 in that order.
        agent.QueueChoice(cands => new object[]
        {
            cands.First(o => ((ICard)o).Name == "b"),
            cands.First(o => ((ICard)o).Name == "c"),
        });
        var player = new Player("P", 20);

        var ordered = await ((IPlayerAgent)agent).ChooseAndOrderFromHandAsync(
            player, new[] { c0, c1, c2 }, count: 2, BotIntent.LibraryReorder);

        ordered.Should().Equal(c1, c2);
    }

    [Fact]
    public async Task ScriptedAgent_OrderedPickN_NoQueuedSelector_FirstCountInOrder()
    {
        var c0 = Card("a");
        var c1 = Card("b");
        var agent = new ScriptedAgent();
        var player = new Player("P", 20);

        var ordered = await ((IPlayerAgent)agent).ChooseAndOrderFromHandAsync(
            player, new[] { c0, c1 }, count: 2, BotIntent.LibraryReorder);

        ordered.Should().Equal(c0, c1);
    }

    [Fact]
    public async Task HeuristicBot_OrderedPickN_ReturnsExactlyCountCards_AllFromCandidates()
    {
        var c0 = new Card("a", "{1}");
        var c1 = new Card("b", "{3}");
        var c2 = new Card("c", "{5}");
        var agent = new Majik.Core.Players.Agents.HeuristicBotAgent();
        var player = new Player("P", 20);

        var ordered = await ((IPlayerAgent)agent).ChooseAndOrderFromHandAsync(
            player, new[] { c0, c1, c2 }, count: 2, BotIntent.LibraryReorder);

        ordered.Should().HaveCount(2);
        ordered.Should().OnlyContain(c => new[] { c0, c1, c2 }.Contains(c));
        ordered.Should().OnlyHaveUniqueItems();
    }
}
