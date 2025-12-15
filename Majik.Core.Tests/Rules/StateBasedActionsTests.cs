using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Unit tests for StateBasedActions service.
/// Tests player loss, creature death, and planeswalker death.
/// </summary>
public class StateBasedActionsTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly StateBasedActions _sba;

    public StateBasedActionsTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _sba = new StateBasedActions(_eventBusMock.Object);
    }

    [Fact]
    public void CheckStateBasedActions_PlayerWithZeroLife_SetsHasLost()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.LoseLife(20);
        // Note: Player.LoseLife already sets HasLost when life reaches 0
        // So we need to reset it to test SBA
        player.HasLost = false;
        var players = new List<Player> { player };
        var cards = new List<ICard>();

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        player.HasLost.Should().BeTrue();
        _eventBusMock.Verify(x => x.Publish(It.IsAny<PlayerLostEvent>()), Times.Once);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StateBasedActionExecutedEvent>()), Times.Once);
    }

    [Fact]
    public void CheckStateBasedActions_PlayerWithNegativeLife_SetsHasLost()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.LoseLife(25);
        var players = new List<Player> { player };
        var cards = new List<ICard>();

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        player.HasLost.Should().BeTrue();
    }

    [Fact]
    public void CheckStateBasedActions_PlayerWithPositiveLife_DoesNotLose()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var players = new List<Player> { player };
        var cards = new List<ICard>();

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        player.HasLost.Should().BeFalse();
    }

    [Fact]
    public void CheckStateBasedActions_DeadCreature_MovesToGraveyard()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player, Controller = player };
        creature.Zone = ZoneType.Battlefield;
        creature.TakeDamage(2);
        var players = new List<Player> { player };
        var cards = new List<ICard> { creature };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        creature.Zone.Should().Be(ZoneType.Graveyard);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StateBasedActionExecutedEvent>()), Times.Once);
    }

    [Fact]
    public void CheckStateBasedActions_DeadPlaneswalker_MovesToGraveyard()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var planeswalker = new Planeswalker("Jace", "2UU", 3) { Owner = player, Controller = player };
        planeswalker.Zone = ZoneType.Battlefield;
        planeswalker.RemoveLoyalty(3);
        var players = new List<Player> { player };
        var cards = new List<ICard> { planeswalker };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        planeswalker.Zone.Should().Be(ZoneType.Graveyard);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StateBasedActionExecutedEvent>()), Times.Once);
    }

    [Fact]
    public void CheckStateBasedActions_NullPlayers_DoesNothing()
    {
        // Arrange
        var cards = new List<ICard>();

        // Act
        _sba.CheckStateBasedActions(null!, cards);

        // Assert
        // Should not throw
    }

    [Fact]
    public void CheckStateBasedActions_NullCards_DoesNothing()
    {
        // Arrange
        var players = new List<Player> { new Player("Alice", 20) };

        // Act
        _sba.CheckStateBasedActions(players, null!);

        // Assert
        // Should not throw
    }
}
