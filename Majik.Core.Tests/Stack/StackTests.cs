using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Stack;
using Moq;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Stack;

/// <summary>
/// Unit tests for Stack class.
/// Tests LIFO operations, events, and encapsulation.
/// </summary>
public class StackTests
{
    [Fact]
    public void Constructor_CreatesEmptyStack()
    {
        // Act
        var stack = new Majik.Core.Stack.Stack();

        // Assert
        stack.IsEmpty.Should().BeTrue();
        stack.Count.Should().Be(0);
        stack.Top.Should().BeNull();
    }

    [Fact]
    public void Push_ValidObject_AddsToStack()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var spell = new Spell(card, player);

        // Act
        stack.Push(spell);

        // Assert
        stack.IsEmpty.Should().BeFalse();
        stack.Count.Should().Be(1);
        stack.Top.Should().Be(spell);
    }

    [Fact]
    public void Push_NullObject_ThrowsException()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();

        // Act & Assert
        stack.Invoking(s => s.Push(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Push_MultipleObjects_AddsInLIFOOrder()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();
        var player = new Player("Alice", 20);
        var card1 = new Instant("Lightning Bolt", "R") { Owner = player };
        var card2 = new Instant("Fireball", "2RR") { Owner = player };
        var spell1 = new Spell(card1, player);
        var spell2 = new Spell(card2, player);

        // Act
        stack.Push(spell1);
        stack.Push(spell2);

        // Assert
        stack.Count.Should().Be(2);
        stack.Top.Should().Be(spell2); // Last in is on top
    }

    [Fact]
    public void Pop_EmptyStack_ReturnsNull()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();

        // Act
        var result = stack.Pop();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Pop_NonEmptyStack_ReturnsTopAndRemoves()
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
        var result = stack.Pop();

        // Assert
        result.Should().Be(spell2);
        stack.Count.Should().Be(1);
        stack.Top.Should().Be(spell1);
    }

    [Fact]
    public void GetAll_ReturnsAllObjectsInOrder()
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
        var all = stack.GetAll();

        // Assert
        all.Should().HaveCount(2);
        // Stack.GetAll() returns from top to bottom
        // The internal stack is reversed, so spell2 (pushed last) is on top
        // After Reverse(), spell1 should be first, spell2 should be last
        // Actually, let's check what the implementation does - it reverses the array
        // So if internal stack has [spell1, spell2] (spell2 on top), 
        // ToArray() gives [spell2, spell1], Reverse() gives [spell1, spell2]
        // So spell1 (first pushed) is first, spell2 (last pushed) is last
        all[0].Should().Be(spell1); // First pushed is first in list
        all[1].Should().Be(spell2); // Last pushed is last in list
    }

    [Fact]
    public void Clear_RemovesAllObjects()
    {
        // Arrange
        var stack = new Majik.Core.Stack.Stack();
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var spell = new Spell(card, player);
        stack.Push(spell);

        // Act
        stack.Clear();

        // Assert
        stack.IsEmpty.Should().BeTrue();
        stack.Count.Should().Be(0);
    }

    [Fact]
    public void Push_PublishesStackObjectAddedEvent()
    {
        // Arrange
        var eventBusMock = new Mock<IEventBus>();
        var stack = new Majik.Core.Stack.Stack(eventBusMock.Object);
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var spell = new Spell(card, player);

        // Act
        stack.Push(spell);

        // Assert
        eventBusMock.Verify(x => x.Publish(It.IsAny<StackObjectAddedEvent>()), Times.Once);
    }

    [Fact]
    public void Clear_PublishesStackClearedEvent()
    {
        // Arrange
        var eventBusMock = new Mock<IEventBus>();
        var stack = new Majik.Core.Stack.Stack(eventBusMock.Object);
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var spell = new Spell(card, player);
        stack.Push(spell);

        // Act
        stack.Clear();

        // Assert
        eventBusMock.Verify(x => x.Publish(It.IsAny<StackClearedEvent>()), Times.Once);
    }
}
