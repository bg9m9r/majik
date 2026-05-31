using FluentAssertions;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// PLAN 01 (Slice C) — <see cref="ChoiceRequest"/> mirrors
/// <see cref="TargetRequest"/>: candidate-gatherer merge/dedupe and
/// <c>WithCandidates</c> substitution.
/// </summary>
public class ChoiceRequestTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext()
    {
        var stack = new Majik.Core.Stack.Stack();
        return new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);
    }

    [Fact]
    public void ResolveCandidates_NoGatherer_ReturnsStaticPool()
    {
        var pool = new object[] { "a", "b" };
        var req = new ChoiceRequest(ChoiceKind.PickOne, "pick", 1, 1, pool);

        req.ResolveCandidates(NewContext()).Should().BeSameAs(pool);
    }

    [Fact]
    public void ResolveCandidates_GathererMerges_AndDedupesByReference()
    {
        var shared = new object();
        var staticPool = new object[] { shared, "static" };
        var gathered = new object[] { shared, "gathered" };
        var req = new ChoiceRequest(
            ChoiceKind.PickN, "pick", 1, 2, staticPool,
            CandidateGatherer: _ => gathered);

        var merged = req.ResolveCandidates(NewContext());

        merged.Should().HaveCount(3);
        merged.Should().Contain(new object[] { shared, "static", "gathered" });
    }

    [Fact]
    public void ResolveCandidates_EmptyStaticPool_ReturnsGathered()
    {
        var gathered = new object[] { "g1", "g2" };
        var req = new ChoiceRequest(
            ChoiceKind.PickOne, "pick", 1, 1, System.Array.Empty<object>(),
            CandidateGatherer: _ => gathered);

        req.ResolveCandidates(NewContext()).Should().BeSameAs(gathered);
    }

    [Fact]
    public void ResolveCandidates_GathererYieldsNothing_ReturnsStaticPool()
    {
        var pool = new object[] { "a" };
        var req = new ChoiceRequest(
            ChoiceKind.PickOne, "pick", 1, 1, pool,
            CandidateGatherer: _ => System.Array.Empty<object>());

        req.ResolveCandidates(NewContext()).Should().BeSameAs(pool);
    }

    [Fact]
    public void WithCandidates_ReplacesPool_AndPreservesOtherFields()
    {
        var req = new ChoiceRequest(
            ChoiceKind.PickN, "pick", 1, 3, new object[] { "old" },
            Intent: BotIntent.Tutor, Optional: true);
        var replaced = req.WithCandidates(new object[] { "new1", "new2" });

        replaced.Candidates.Should().Equal("new1", "new2");
        replaced.Kind.Should().Be(ChoiceKind.PickN);
        replaced.Description.Should().Be("pick");
        replaced.Min.Should().Be(1);
        replaced.Max.Should().Be(3);
        replaced.Intent.Should().Be(BotIntent.Tutor);
        replaced.Optional.Should().BeTrue();
    }
}
