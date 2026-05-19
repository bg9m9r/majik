using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Unit tests for Planeswalker uniqueness rule state-based action (Rule 704.5m).
/// </summary>
public class PlaneswalkerUniquenessTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly ZoneService _zoneService;
    private readonly StateBasedActions _sba;

    public PlaneswalkerUniquenessTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _zoneService = new ZoneService(_eventBusMock.Object);
        _sba = new StateBasedActions(_eventBusMock.Object, _zoneService);
    }

    [Fact]
    public void CheckPlaneswalkerUniqueness_TwoJaces_SendsOneToGraveyard()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var jace1 = new Planeswalker("Jace, Vryn's Prodigy", "1U", 3, 
            subtypes: new[] { CardSubtype.Jace }) { Owner = player, Controller = player };
        var jace2 = new Planeswalker("Jace, the Mind Sculptor", "2UU", 4, 
            subtypes: new[] { CardSubtype.Jace }) { Owner = player, Controller = player };
        
        jace1.SetZone(ZoneType.Battlefield);
        jace2.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(jace1, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(jace2, ZoneType.Battlefield, player);

        var players = new List<Player> { player };
        var cards = new List<ICard> { jace1, jace2 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        var onBattlefield = cards.Where(c => c.Zone == ZoneType.Battlefield).ToList();
        var inGraveyard = cards.Where(c => c.Zone == ZoneType.Graveyard).ToList();
        
        onBattlefield.Should().HaveCount(1); // One should remain
        inGraveyard.Should().HaveCount(1); // One should be in graveyard
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StateBasedActionExecutedEvent>()), Times.AtLeastOnce);
    }

    [Fact]
    public void CheckPlaneswalkerUniqueness_TwoDifferentSubtypes_NoAction()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var jace = new Planeswalker("Jace, Vryn's Prodigy", "1U", 3, 
            subtypes: new[] { CardSubtype.Jace }) { Owner = player, Controller = player };
        var liliana = new Planeswalker("Liliana, Heretical Healer", "1B", 3, 
            subtypes: new[] { CardSubtype.Liliana }) { Owner = player, Controller = player };
        
        jace.SetZone(ZoneType.Battlefield);
        liliana.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(jace, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(liliana, ZoneType.Battlefield, player);

        var players = new List<Player> { player };
        var cards = new List<ICard> { jace, liliana };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        cards.All(c => c.Zone == ZoneType.Battlefield).Should().BeTrue();
        _eventBusMock.Verify(x => x.Publish(It.Is<StateBasedActionExecutedEvent>(e => 
            e.ActionDescription.Contains("Planeswalker uniqueness"))), Times.Never);
    }

    [Fact]
    public void CheckPlaneswalkerUniqueness_TwoJacesDifferentControllers_NoAction()
    {
        // Arrange
        var player1 = new Player("Alice", 20);
        var player2 = new Player("Bob", 20);
        var jace1 = new Planeswalker("Jace, Vryn's Prodigy", "1U", 3, 
            subtypes: new[] { CardSubtype.Jace }) { Owner = player1, Controller = player1 };
        var jace2 = new Planeswalker("Jace, the Mind Sculptor", "2UU", 4, 
            subtypes: new[] { CardSubtype.Jace }) { Owner = player2, Controller = player2 };
        
        jace1.SetZone(ZoneType.Battlefield);
        jace2.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(jace1, ZoneType.Battlefield, player1);
        _zoneService.MoveCardTo(jace2, ZoneType.Battlefield, player2);

        var players = new List<Player> { player1, player2 };
        var cards = new List<ICard> { jace1, jace2 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        cards.All(c => c.Zone == ZoneType.Battlefield).Should().BeTrue();
        _eventBusMock.Verify(x => x.Publish(It.Is<StateBasedActionExecutedEvent>(e => 
            e.ActionDescription.Contains("Planeswalker uniqueness"))), Times.Never);
    }

    [Fact]
    public void CheckPlaneswalkerUniqueness_ThreeJaces_SendsTwoToGraveyard()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var jace1 = new Planeswalker("Jace, Vryn's Prodigy", "1U", 3, 
            subtypes: new[] { CardSubtype.Jace }) { Owner = player, Controller = player };
        var jace2 = new Planeswalker("Jace, the Mind Sculptor", "2UU", 4, 
            subtypes: new[] { CardSubtype.Jace }) { Owner = player, Controller = player };
        var jace3 = new Planeswalker("Jace, Unraveler of Secrets", "3UU", 5, 
            subtypes: new[] { CardSubtype.Jace }) { Owner = player, Controller = player };
        
        jace1.SetZone(ZoneType.Battlefield);
        jace2.SetZone(ZoneType.Battlefield);
        jace3.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(jace1, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(jace2, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(jace3, ZoneType.Battlefield, player);

        var players = new List<Player> { player };
        var cards = new List<ICard> { jace1, jace2, jace3 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        var onBattlefield = cards.Where(c => c.Zone == ZoneType.Battlefield).ToList();
        var inGraveyard = cards.Where(c => c.Zone == ZoneType.Graveyard).ToList();
        
        onBattlefield.Should().HaveCount(1); // One should remain
        inGraveyard.Should().HaveCount(2); // Two should be in graveyard
    }
}
