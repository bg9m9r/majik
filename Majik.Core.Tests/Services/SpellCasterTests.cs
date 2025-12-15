using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Stack;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Services;

/// <summary>
/// Unit tests for SpellCaster service.
/// Tests spell casting validation, cost payment, and event publishing.
/// </summary>
public class SpellCasterTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCaster _spellCaster;

    public SpellCasterTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _stack = new Majik.Core.Stack.Stack(_eventBusMock.Object);
        _spellCaster = new SpellCaster(_stack, _eventBusMock.Object);
    }

    [Fact]
    public void CanCast_CardInHand_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        player.Zones.Hand.AddCard(card);

        // Act
        var result = _spellCaster.CanCast(card, player, true, true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanCast_CardNotInHand_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        card.Zone = ZoneType.Graveyard;

        // Act
        var result = _spellCaster.CanCast(card, player, true, true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanCast_SorceryNotMainPhase_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Sorcery("Fireball", "2RR") { Owner = player };
        player.Zones.Hand.AddCard(card);

        // Act
        var result = _spellCaster.CanCast(card, player, false, true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanCast_SorceryStackNotEmpty_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Sorcery("Fireball", "2RR") { Owner = player };
        player.Zones.Hand.AddCard(card);

        // Act
        var result = _spellCaster.CanCast(card, player, true, false);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanCast_NullCard_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        var result = _spellCaster.CanCast(null!, player, true, true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanCast_NullPlayer_ReturnsFalse()
    {
        // Arrange
        var card = new Instant("Lightning Bolt", "R");

        // Act
        var result = _spellCaster.CanCast(card, null!, true, true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CastSpell_ValidSpell_AddsToStack()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        player.Zones.Hand.AddCard(card);
        player.AddManaToPool(ManaCost.Parse("R"));

        // Act
        _spellCaster.CastSpell(card, player);

        // Assert
        _stack.Count.Should().Be(1);
        _stack.Top.Should().NotBeNull();
        card.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public void CastSpell_ValidSpell_PaysCosts()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        player.Zones.Hand.AddCard(card);
        player.AddManaToPool(ManaCost.Parse("R"));

        // Act
        _spellCaster.CastSpell(card, player);

        // Assert
        player.ManaPool.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void CastSpell_ValidSpell_PublishesEvents()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        player.Zones.Hand.AddCard(card);
        player.AddManaToPool(ManaCost.Parse("R"));

        // Act
        _spellCaster.CastSpell(card, player);

        // Assert
        _eventBusMock.Verify(x => x.Publish(It.IsAny<SpellCastEvent>()), Times.Once);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<CostsPaidEvent>()), Times.Once);
    }

    [Fact]
    public void CastSpell_InsufficientMana_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        player.Zones.Hand.AddCard(card);
        // No mana added

        // Act & Assert
        _spellCaster.Invoking(s => s.CastSpell(card, player))
            .Should().Throw<InvalidPlayerActionException>();
    }

    [Fact]
    public void CastSpell_InvalidTiming_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Sorcery("Fireball", "2RR") { Owner = player };
        player.Zones.Hand.AddCard(card);
        player.AddManaToPool(ManaCost.Parse("2RR"));

        // Act & Assert
        _spellCaster.Invoking(s => s.CastSpell(card, player, null, null, false, true))
            .Should().Throw<InvalidPlayerActionException>();
    }

    [Fact]
    public void CastSpell_NullCard_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act & Assert
        _spellCaster.Invoking(s => s.CastSpell(null!, player))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CastSpell_NullPlayer_ThrowsException()
    {
        // Arrange
        var card = new Instant("Lightning Bolt", "R");

        // Act & Assert
        _spellCaster.Invoking(s => s.CastSpell(card, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CastSpell_WithTargets_PublishesTargetsChosenEvent()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        player.Zones.Hand.AddCard(card);
        player.AddManaToPool(ManaCost.Parse("R"));
        var targets = new List<Targeting.ITarget> { Targeting.Target.Player(targetPlayer) };

        // Act
        _spellCaster.CastSpell(card, player, targets);

        // Assert
        _eventBusMock.Verify(x => x.Publish(It.IsAny<TargetsChosenEvent>()), Times.Once);
    }
}
