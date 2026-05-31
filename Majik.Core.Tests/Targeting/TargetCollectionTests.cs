using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.TargetingPipeline;

/// <summary>
/// PLAN 01 (Slice E) — the shared <see cref="TargetCollection.CollectAsync"/>
/// pipeline used by the spell-cast, activated-ability, and triggered-ability
/// paths. Verifies the gatherer merge, the min-cardinality throw on the
/// spell-style path, the lenient (no-throw) ability/trigger path, and the
/// null-agent (empty pick) trigger path.
/// </summary>
public class TargetCollectionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext()
    {
        var stack = new Majik.Core.Stack.Stack();
        return new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);
    }

    /// <summary>Agent that returns a fixed pick for the next ChooseTargetsAsync,
    /// and records the candidate pool it was offered. Only the targeting sink
    /// is exercised by these tests; the other members throw.</summary>
    private sealed class FixedTargetAgent : IPlayerAgent
    {
        public IReadOnlyList<object> OfferedCandidates { get; private set; } = System.Array.Empty<object>();
        private readonly IReadOnlyList<object> _pick;
        public FixedTargetAgent(IReadOnlyList<object> pick) { _pick = pick; }

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        {
            OfferedCandidates = request.LegalCandidates;
            return Task.FromResult(_pick);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new System.NotSupportedException();
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
    public async Task CollectAsync_MergesGathererCandidates_BeforePrompting()
    {
        var c0 = Card("static");
        var gathered = Card("gathered");
        var agent = new FixedTargetAgent(new object[] { c0 });
        var req = new TargetRequest(
            "target", MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { c0 },
            CandidateGatherer: _ => new object[] { gathered });

        await TargetCollection.CollectAsync(new[] { req }, c0, NewContext(), agent);

        agent.OfferedCandidates.Should().Contain(new object[] { c0, gathered });
    }

    [Fact]
    public async Task CollectAsync_SpellPath_ThrowsWhenBelowMinCardinality()
    {
        var c0 = Card("c");
        var agent = new FixedTargetAgent(System.Array.Empty<object>()); // picks nothing
        var req = new TargetRequest("target", MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { c0 });

        var act = async () => await TargetCollection.CollectAsync(
            new[] { req }, c0, NewContext(), agent, throwOnInsufficient: true);

        await act.Should().ThrowAsync<System.InvalidOperationException>();
    }

    [Fact]
    public async Task CollectAsync_AbilityPath_AcceptsUnderfilledPick()
    {
        var c0 = Card("c");
        var agent = new FixedTargetAgent(System.Array.Empty<object>());
        var req = new TargetRequest("target", MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { c0 });

        var collected = await TargetCollection.CollectAsync(
            new[] { req }, c0, NewContext(), agent, throwOnInsufficient: false);

        collected.Should().HaveCount(1);
        collected[0].Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_NullAgent_ResolvesEveryRequestToEmpty()
    {
        var req = new TargetRequest("target", MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { Card("c") });

        var collected = await TargetCollection.CollectAsync(
            new[] { req }, card: null, NewContext(), agent: null, throwOnInsufficient: false);

        collected.Should().HaveCount(1);
        collected[0].Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_NoRequests_ReturnsEmpty()
    {
        var collected = await TargetCollection.CollectAsync(
            System.Array.Empty<TargetRequest>(), null, NewContext(), agent: null);

        collected.Should().BeEmpty();
    }
}
