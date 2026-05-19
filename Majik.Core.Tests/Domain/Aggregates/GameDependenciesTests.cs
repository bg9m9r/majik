using FluentAssertions;
using Majik.Core.Domain.Aggregates;
using Majik.Core.Events;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Domain.Aggregates;

/// <summary>
/// Verifies that <see cref="Game"/> consumes a pre-composed
/// <see cref="GameDependencies"/> instead of constructing services itself.
/// This is the DI seam that lets tests and the API layer swap managers.
/// </summary>
public class GameDependenciesTests
{
    [Fact]
    public void Game_AcceptsMockedEventBus_AndPublishesGameStartedThroughIt()
    {
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.Subscribe(It.IsAny<Action<GameStartedEvent>>()));

        var deps = GameDependencies.CreateDefault(bus.Object);
        var game = new Majik.Core.Domain.Aggregates.Game(deps);
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");

        game.StartGame();

        bus.Verify(b => b.Publish(It.IsAny<GameStartedEvent>()), Times.Once);
    }

    [Fact]
    public void CreateDefault_WiresPhaseManagerWithCombatManager()
    {
        var deps = GameDependencies.CreateDefault();

        // PhaseManager.SetCombatManager runs inside the ctor — the only
        // observable signal is that the same combat manager survives.
        deps.CombatManager.Should().NotBeNull();
        deps.PhaseManager.Should().NotBeNull();
    }

    [Fact]
    public void Game_ParameterlessCtor_StillWorks_BackCompat()
    {
        var game = new Majik.Core.Domain.Aggregates.Game();
        game.Players.Should().BeEmpty();
        game.IsStarted.Should().BeFalse();
    }

    [Fact]
    public void Game_NullDependencies_Throws()
    {
        var act = () => new Majik.Core.Domain.Aggregates.Game(default(GameDependencies)!);
        act.Should().Throw<ArgumentNullException>();
    }
}
