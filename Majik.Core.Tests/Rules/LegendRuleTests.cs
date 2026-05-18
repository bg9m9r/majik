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
/// Unit tests for Legend rule state-based action (Rule 704.5k).
/// </summary>
public class LegendRuleTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly ZoneService _zoneService;
    private readonly StateBasedActions _sba;

    public LegendRuleTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _zoneService = new ZoneService(_eventBusMock.Object);
        _sba = new StateBasedActions(_eventBusMock.Object, _zoneService);
    }

    [Fact]
    public void CheckLegendRule_TwoLegendaryCreaturesSameName_SendsOneToGraveyard()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature1 = new Creature("Jace, Vryn's Prodigy", "1U", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player, Controller = player };
        var creature2 = new Creature("Jace, Vryn's Prodigy", "1U", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player, Controller = player };
        
        creature1.Zone = ZoneType.Battlefield;
        creature2.Zone = ZoneType.Battlefield;
        _zoneService.MoveCardTo(creature1, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(creature2, ZoneType.Battlefield, player);

        var players = new List<Player> { player };
        var cards = new List<ICard> { creature1, creature2 };

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
    public void CheckLegendRule_TwoLegendaryCreaturesDifferentNames_NoAction()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature1 = new Creature("Jace, Vryn's Prodigy", "1U", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player, Controller = player };
        var creature2 = new Creature("Liliana, Heretical Healer", "1B", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player, Controller = player };
        
        creature1.Zone = ZoneType.Battlefield;
        creature2.Zone = ZoneType.Battlefield;
        _zoneService.MoveCardTo(creature1, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(creature2, ZoneType.Battlefield, player);

        var players = new List<Player> { player };
        var cards = new List<ICard> { creature1, creature2 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        cards.All(c => c.Zone == ZoneType.Battlefield).Should().BeTrue();
        _eventBusMock.Verify(x => x.Publish(It.Is<StateBasedActionExecutedEvent>(e => 
            e.ActionDescription.Contains("Legend rule"))), Times.Never);
    }

    [Fact]
    public void CheckLegendRule_TwoLegendaryCreaturesDifferentControllers_NoAction()
    {
        // Arrange
        var player1 = new Player("Alice", 20);
        var player2 = new Player("Bob", 20);
        var creature1 = new Creature("Jace, Vryn's Prodigy", "1U", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player1, Controller = player1 };
        var creature2 = new Creature("Jace, Vryn's Prodigy", "1U", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player2, Controller = player2 };
        
        creature1.Zone = ZoneType.Battlefield;
        creature2.Zone = ZoneType.Battlefield;
        _zoneService.MoveCardTo(creature1, ZoneType.Battlefield, player1);
        _zoneService.MoveCardTo(creature2, ZoneType.Battlefield, player2);

        var players = new List<Player> { player1, player2 };
        var cards = new List<ICard> { creature1, creature2 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        cards.All(c => c.Zone == ZoneType.Battlefield).Should().BeTrue();
        _eventBusMock.Verify(x => x.Publish(It.Is<StateBasedActionExecutedEvent>(e => 
            e.ActionDescription.Contains("Legend rule"))), Times.Never);
    }

    [Fact]
    public void CheckLegendRule_ThreeLegendaryCreaturesSameName_SendsTwoToGraveyard()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature1 = new Creature("Jace, Vryn's Prodigy", "1U", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player, Controller = player };
        var creature2 = new Creature("Jace, Vryn's Prodigy", "1U", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player, Controller = player };
        var creature3 = new Creature("Jace, Vryn's Prodigy", "1U", 0, 1, 
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player, Controller = player };
        
        creature1.Zone = ZoneType.Battlefield;
        creature2.Zone = ZoneType.Battlefield;
        creature3.Zone = ZoneType.Battlefield;
        _zoneService.MoveCardTo(creature1, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(creature2, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(creature3, ZoneType.Battlefield, player);

        var players = new List<Player> { player };
        var cards = new List<ICard> { creature1, creature2, creature3 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        var onBattlefield = cards.Where(c => c.Zone == ZoneType.Battlefield).ToList();
        var inGraveyard = cards.Where(c => c.Zone == ZoneType.Graveyard).ToList();
        
        onBattlefield.Should().HaveCount(1); // One should remain
        inGraveyard.Should().HaveCount(2); // Two should be in graveyard
    }

    [Fact]
    public void CheckLegendRule_NonLegendaryCreatures_NoAction()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature1 = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player, Controller = player };
        var creature2 = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player, Controller = player };
        
        creature1.Zone = ZoneType.Battlefield;
        creature2.Zone = ZoneType.Battlefield;
        _zoneService.MoveCardTo(creature1, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(creature2, ZoneType.Battlefield, player);

        var players = new List<Player> { player };
        var cards = new List<ICard> { creature1, creature2 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert
        cards.All(c => c.Zone == ZoneType.Battlefield).Should().BeTrue();
        _eventBusMock.Verify(x => x.Publish(It.Is<StateBasedActionExecutedEvent>(e => 
            e.ActionDescription.Contains("Legend rule"))), Times.Never);
    }
}
