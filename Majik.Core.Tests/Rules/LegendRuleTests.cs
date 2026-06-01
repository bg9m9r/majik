using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
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
        
        creature1.SetZone(ZoneType.Battlefield);
        creature2.SetZone(ZoneType.Battlefield);
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
    public void CheckLegendRule_CloneCopiesLegendarySupertype_TriggersLegendRule()
    {
        // CR 707.2 — supertypes are copiable. A non-legendary permanent that
        // becomes a copy of a Legendary source (via CopyCharacteristicsEffect)
        // gains the Legendary supertype and, sharing a name with an existing
        // legend, triggers the legend-rule SBA. Without the copy-supertype fix
        // the second permanent stays non-legendary and no SBA fires.
        var player = new Player("Alice", 20);
        var svc = new ContinuousEffectsService();

        var printedLegend = new Creature("Kaheera", "1G", 3, 2,
            supertypes: new[] { CardSupertype.Legendary }) { Owner = player, Controller = player };

        // Same name, but printed NON-legendary; it becomes a copy of a Legendary
        // source so its EFFECTIVE supertype set gains Legendary.
        var copier = new Creature("Kaheera", "1G", 3, 2) { Owner = player, Controller = player };
        copier.ActiveEffects = svc;
        var legendarySource = new Creature("Some Legend", "1G", 3, 2,
            supertypes: new[] { CardSupertype.Legendary });

        printedLegend.SetZone(ZoneType.Battlefield);
        copier.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(printedLegend, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(copier, ZoneType.Battlefield, player);

        svc.Register(new CopyCharacteristicsEffect(copier, legendarySource));
        copier.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue(
            "the copy effect confers the source's Legendary supertype");

        var players = new List<Player> { player };
        var cards = new List<ICard> { printedLegend, copier };

        _sba.CheckStateBasedActions(players, cards);

        cards.Count(c => c.Zone == ZoneType.Battlefield).Should().Be(1,
            "two same-named legendaries (one via copied supertype) → legend rule keeps one");
        cards.Count(c => c.Zone == ZoneType.Graveyard).Should().Be(1);
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
        
        creature1.SetZone(ZoneType.Battlefield);
        creature2.SetZone(ZoneType.Battlefield);
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
        
        creature1.SetZone(ZoneType.Battlefield);
        creature2.SetZone(ZoneType.Battlefield);
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
        
        creature1.SetZone(ZoneType.Battlefield);
        creature2.SetZone(ZoneType.Battlefield);
        creature3.SetZone(ZoneType.Battlefield);
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

    // -----------------------------------------------------------------------
    // Deferral #4 — GrantSupertypeEffect (Layer-4 "is legendary") interacts
    // with the legend rule via the EFFECTIVE supertype set (CR 205.4 / 704.5k).
    // -----------------------------------------------------------------------

    [Fact]
    public void CheckLegendRule_SecondPermanentGrantedLegendary_TriggersLegendRule()
    {
        // Arrange — two same-name permanents that are NOT printed legendary.
        // The second is granted Legendary by an active GrantSupertypeEffect
        // (the Ring-bearer "is legendary" shape). The legend rule reads the
        // effective supertype set, so BOTH now count as legendary → one dies.
        var effects = new ContinuousEffectsService();
        var player = new Player("Alice", 20);
        var c1 = new Creature("Mishra's Helix", "1U", 0, 1) { Owner = player, Controller = player };
        var c2 = new Creature("Mishra's Helix", "1U", 0, 1) { Owner = player, Controller = player };
        c1.ActiveEffects = effects;
        c2.ActiveEffects = effects;

        c1.SetZone(ZoneType.Battlefield);
        c2.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(c1, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(c2, ZoneType.Battlefield, player);

        // Grant Legendary to BOTH so the legend rule applies to the pair.
        effects.Register(GrantSupertypeEffect.ForPermanent(c1, CardSupertype.Legendary));
        effects.Register(GrantSupertypeEffect.ForPermanent(c2, CardSupertype.Legendary));

        c1.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue();
        c2.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue();

        var players = new List<Player> { player };
        var cards = new List<ICard> { c1, c2 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert — exactly one survives, one goes to the graveyard.
        cards.Count(c => c.Zone == ZoneType.Battlefield).Should().Be(1);
        cards.Count(c => c.Zone == ZoneType.Graveyard).Should().Be(1);
    }

    [Fact]
    public void CheckLegendRule_GrantRevokedWhenSourceLeaves_NoLegendRule()
    {
        // Arrange — two same-name non-legendary permanents. A grant makes one
        // legendary, but its SOURCE has left the battlefield, so the effect is
        // inactive (IsActive() gates on source zone). With only one effective
        // legendary the legend rule does NOT fire.
        var effects = new ContinuousEffectsService();
        var player = new Player("Alice", 20);
        var c1 = new Creature("Mishra's Helix", "1U", 0, 1) { Owner = player, Controller = player };
        var c2 = new Creature("Mishra's Helix", "1U", 0, 1) { Owner = player, Controller = player };
        c1.ActiveEffects = effects;
        c2.ActiveEffects = effects;

        c1.SetZone(ZoneType.Battlefield);
        c2.SetZone(ZoneType.Battlefield);
        _zoneService.MoveCardTo(c1, ZoneType.Battlefield, player);
        _zoneService.MoveCardTo(c2, ZoneType.Battlefield, player);

        // A grant whose source (a third permanent) is NOT on the battlefield.
        var grantSource = new Creature("Crown", "1", 0, 1) { Owner = player, Controller = player };
        grantSource.ActiveEffects = effects;
        grantSource.SetZone(ZoneType.Graveyard); // source off battlefield → grant inactive
        effects.Register(new GrantSupertypeEffect(
            grantSource, p => ReferenceEquals(p, c1), CardSupertype.Legendary));

        c1.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeFalse(
            "the grant's source is off the battlefield, so the effect is inactive");

        var players = new List<Player> { player };
        var cards = new List<ICard> { c1, c2 };

        // Act
        _sba.CheckStateBasedActions(players, cards);

        // Assert — both survive; legend rule never applied.
        cards.All(c => c.Zone == ZoneType.Battlefield).Should().BeTrue();
        _eventBusMock.Verify(x => x.Publish(It.Is<StateBasedActionExecutedEvent>(e =>
            e.ActionDescription.Contains("Legend rule"))), Times.Never);
    }

    [Fact]
    public void CheckLegendRule_NonLegendaryCreatures_NoAction()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature1 = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player, Controller = player };
        var creature2 = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player, Controller = player };
        
        creature1.SetZone(ZoneType.Battlefield);
        creature2.SetZone(ZoneType.Battlefield);
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
