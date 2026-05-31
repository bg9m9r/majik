using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// PLAN 01 (Slice A) — <see cref="ResolutionContext"/> carries the controller,
/// agent, game, and chosen targets to an async effect body.
/// </summary>
public class ResolutionContextTests
{
    [Fact]
    public void For_CarriesControllerAgentAndTargets()
    {
        var controller = new Player("P", 20);
        var agent = new ScriptedAgent();
        var targets = new IReadOnlyList<object>[] { new object[] { "t" } };

        var rc = ResolutionContext.For(controller, agent, game: null, targets);

        rc.Controller.Should().BeSameAs(controller);
        rc.Agent.Should().BeSameAs(agent);
        rc.Game.Should().BeNull();
        rc.ChosenTargets.Should().BeSameAs(targets);
    }

    [Fact]
    public void For_NullTargets_DefaultsToEmpty()
    {
        var controller = new Player("P", 20);

        var rc = ResolutionContext.For(controller, agent: null, game: null, chosenTargets: null);

        rc.ChosenTargets.Should().BeEmpty();
    }

    [Fact]
    public void Legacy_IsContextFree()
    {
        ResolutionContext.Legacy.Agent.Should().BeNull();
        ResolutionContext.Legacy.Game.Should().BeNull();
        ResolutionContext.Legacy.ChosenTargets.Should().BeEmpty();
    }
}
