using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests;

public class TargetPolicyTests
{
    [Fact]
    public void Pick_OneTarget_PicksHighestPowerCreature()
    {
        var s = new BotTestScenario();
        var goblin = s.AddCreatureToBattlefield(s.Opponent, "Goblin", 1, 1);
        var wurm   = s.AddCreatureToBattlefield(s.Opponent, "Wurm", 6, 6);

        var req = new TargetRequest(
            Description: "destroy target creature",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { goblin, wurm });

        var picked = TargetPolicy.Pick(s.Context, s.Self, req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(wurm);
    }

    [Fact]
    public void Pick_PicksFromLegalCandidates_NotOpponentBoardScan()
    {
        var s = new BotTestScenario();
        var big   = s.AddCreatureToBattlefield(s.Opponent, "Big", 5, 5);
        var small = s.AddCreatureToBattlefield(s.Opponent, "Small", 1, 1);
        var req = new TargetRequest("only this one", 1, 1, new object[] { small });
        var picked = TargetPolicy.Pick(s.Context, s.Self, req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(small);
    }

    [Fact]
    public void Pick_ZeroLegalCandidates_ReturnsEmpty()
    {
        var s = new BotTestScenario();
        var req = new TargetRequest("nothing", 0, 1, Array.Empty<object>());
        TargetPolicy.Pick(s.Context, s.Self, req).Should().BeEmpty();
    }

    /// <summary>
    /// Empty candidate pool + REQUIRED target: synthesize engine-side defaults
    /// (mirrors HeuristicBotAgent's fallback) instead of returning an empty
    /// pick that makes every such cast illegal under CR 601.2c. The damage-any
    /// spell templates build TargetRequest("any target", 1, 1, []) with no
    /// gathering pass — without this, Lightning Bolt could never be cast by
    /// any TargetPolicy-backed bot seat (live or sandbox).
    /// </summary>
    [Fact]
    public void Pick_EmptyCandidates_RequiredAnyTarget_SynthesizesOpponentFace()
    {
        var s = new BotTestScenario();
        var req = new TargetRequest("any target", 1, 1, Array.Empty<object>());
        TargetPolicy.Pick(s.Context, s.Self, req)
            .Should().ContainSingle().Which.Should().BeSameAs(s.Opponent);
    }

    [Fact]
    public void Pick_EmptyCandidates_RequiredCreature_SynthesizesBiggestEnemyCreature()
    {
        var s = new BotTestScenario();
        var small = s.AddCreatureToBattlefield(s.Opponent, "Small", 1, 1);
        var big   = s.AddCreatureToBattlefield(s.Opponent, "Big", 5, 5);
        _ = small;

        var req = new TargetRequest("destroy target creature", 1, 1, Array.Empty<object>());
        TargetPolicy.Pick(s.Context, s.Self, req)
            .Should().ContainSingle().Which.Should().BeSameAs(big);
    }

    [Fact]
    public void Pick_EmptyCandidates_RequiredCreature_EmptyBoard_ReturnsUnderfilled()
    {
        var s = new BotTestScenario();
        var req = new TargetRequest("destroy target creature", 1, 1, Array.Empty<object>());
        // Nothing to synthesize — under-filled pick correctly makes the cast
        // illegal (CR 601.2c) and the engine skips it.
        TargetPolicy.Pick(s.Context, s.Self, req).Should().BeEmpty();
    }

    [Fact]
    public void Pick_RespectsMaxTargets()
    {
        var s = new BotTestScenario();
        var c1 = s.AddCreatureToBattlefield(s.Opponent, "C1", 3, 3);
        var c2 = s.AddCreatureToBattlefield(s.Opponent, "C2", 2, 2);
        var req = new TargetRequest("up to one", 0, 1, new object[] { c1, c2 });
        var picked = TargetPolicy.Pick(s.Context, s.Self, req);
        picked.Should().HaveCount(1);
    }
}
