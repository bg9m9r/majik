using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Stack;
using Majik.Core.Zones;
using Moq;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Services;

/// <summary>
/// Unit tests for StackResolver service.
/// Tests stack resolution, zone movement, and event publishing.
/// </summary>
public class StackResolverTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly StackResolver _resolver;

    public StackResolverTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _resolver = new StackResolver(_eventBusMock.Object);
    }

    [Fact]
    public void ResolveTop_EmptyStack_ReturnsNull()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();

        // Act
        var result = _resolver.ResolveTop(stack);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveTop_SpellOnStack_ResolvesAndMovesToGraveyard()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var spell = new Spell(card, player);
        stack.Push(spell);

        // Act
        var result = _resolver.ResolveTop(stack);

        // Assert
        result.Should().Be(spell);
        card.Zone.Should().Be(ZoneType.Graveyard);
        stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ResolveTop_CreatureSpell_MovesToBattlefield()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();
        var player = new Player("Alice", 20);
        var card = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player };
        var spell = new Spell(card, player);
        stack.Push(spell);

        // Act
        _resolver.ResolveTop(stack);

        // Assert
        card.Zone.Should().Be(ZoneType.Battlefield);
        card.Controller.Should().Be(player);
    }

    [Fact]
    public void ResolveTop_PublishesResolvedEvent()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var spell = new Spell(card, player);
        stack.Push(spell);

        // Act
        _resolver.ResolveTop(stack);

        // Assert
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StackObjectResolvedEvent>()), Times.Once);
    }

    [Fact]
    public void ResolveTop_NullStack_ThrowsException()
    {
        // Act & Assert
        _resolver.Invoking(r => r.ResolveTop(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CanResolve_EmptyStack_ReturnsFalse()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();

        // Act
        var result = _resolver.CanResolve(stack);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanResolve_NonEmptyStack_ReturnsTrue()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var spell = new Spell(card, player);
        stack.Push(spell);

        // Act
        var result = _resolver.CanResolve(stack);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanResolve_NullStack_ReturnsFalse()
    {
        // Act
        var result = _resolver.CanResolve(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveAll_ResolvesAllObjects()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();
        var player = new Player("Alice", 20);
        var card1 = new Instant("Lightning Bolt", "R") { Owner = player };
        var card2 = new Instant("Fireball", "2RR") { Owner = player };
        var spell1 = new Spell(card1, player);
        var spell2 = new Spell(card2, player);
        stack.Push(spell1);
        stack.Push(spell2);

        // Act
        _resolver.ResolveAll(stack);

        // Assert
        stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ResolveAll_NullStack_ThrowsException()
    {
        // Act & Assert
        _resolver.Invoking(r => r.ResolveAll(null!))
            .Should().Throw<ArgumentNullException>();
    }
}
