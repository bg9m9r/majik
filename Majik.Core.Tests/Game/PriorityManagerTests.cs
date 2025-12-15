using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Stack;
using Moq;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Unit tests for PriorityManager.
/// Tests priority passing, initialization, and state management.
/// </summary>
public class PriorityManagerTests
{
    private readonly List<Player> _players;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly PriorityManager _priorityManager;

    public PriorityManagerTests()
    {
        _players = new List<Player>
        {
            new Player("Alice", 20),
            new Player("Bob", 20)
        };
        _eventBusMock = new Mock<IEventBus>();
        _stack = new Majik.Core.Stack.Stack(_eventBusMock.Object);
        _priorityManager = new PriorityManager(_players, _stack, _eventBusMock.Object);
    }

    [Fact]
    public void Constructor_ValidInput_CreatesPriorityManager()
    {
        // Arrange
        var players = new List<Player>
        {
            new Player("Alice", 20),
            new Player("Bob", 20)
        };
        var stack = new Majik.Core.Stack.Stack();

        // Act
        var manager = new PriorityManager(players, stack);

        // Assert
        manager.CurrentPlayer.Should().BeNull();
        manager.AllPlayersPassed.Should().BeFalse();
    }

    [Fact]
    public void Constructor_LessThanTwoPlayers_ThrowsException()
    {
        // Arrange
        var players = new List<Player> { new Player("Alice", 20) };
        var stack = new Majik.Core.Stack.Stack();

        // Act & Assert
        new Action(() => new PriorityManager(players, stack))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullStack_ThrowsException()
    {
        // Arrange
        var players = new List<Player>
        {
            new Player("Alice", 20),
            new Player("Bob", 20)
        };

        // Act & Assert
        new Action(() => new PriorityManager(players, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void InitializeForPhase_ValidPlayer_SetsInitialState()
    {
        // Arrange
        var alice = _players[0];

        // Act
        _priorityManager.InitializeForPhase(alice);

        // Assert
        _priorityManager.CurrentPlayer.Should().Be(alice);
        _priorityManager.AllPlayersPassed.Should().BeFalse();
        _eventBusMock.Verify(x => x.Publish(It.IsAny<PriorityReceivedEvent>()), Times.Once);
    }

    [Fact]
    public void InitializeForPhase_NullPlayer_ThrowsException()
    {
        // Act & Assert
        _priorityManager.Invoking(p => p.InitializeForPhase(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void InitializeForPhase_PlayerNotInGame_ThrowsException()
    {
        // Arrange
        var outsider = new Player("Outsider", 20);

        // Act & Assert
        _priorityManager.Invoking(p => p.InitializeForPhase(outsider))
            .Should().Throw<InvalidGameStateException>();
    }

    [Fact]
    public void GivePriority_ValidPlayer_SetsCurrentPlayer()
    {
        // Arrange
        var alice = _players[0];
        var bob = _players[1];
        _priorityManager.InitializeForPhase(alice);

        // Act
        _priorityManager.GivePriority(bob);

        // Assert
        _priorityManager.CurrentPlayer.Should().Be(bob);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<PriorityReceivedEvent>()), Times.Exactly(2));
    }

    [Fact]
    public void PassPriority_IncrementsPassCount()
    {
        // Arrange
        var alice = _players[0];
        _priorityManager.InitializeForPhase(alice);

        // Act
        _priorityManager.PassPriority();

        // Assert
        _priorityManager.CurrentPlayer.Should().Be(_players[1]); // Next player
        _eventBusMock.Verify(x => x.Publish(It.IsAny<PriorityPassedEvent>()), Times.Once);
    }

    [Fact]
    public void PassPriority_AllPlayersPass_SetsAllPlayersPassed()
    {
        // Arrange
        var alice = _players[0];
        _priorityManager.InitializeForPhase(alice);

        // Act
        _priorityManager.PassPriority(); // Bob passes
        _priorityManager.PassPriority(); // Back to Alice, she passes

        // Assert
        _priorityManager.AllPlayersPassed.Should().BeTrue();
    }

    [Fact]
    public void PassPriority_NoCurrentPlayer_ThrowsException()
    {
        // Act & Assert
        _priorityManager.Invoking(p => p.PassPriority())
            .Should().Throw<InvalidGameStateException>();
    }

    [Fact]
    public void CanEndPhase_StackEmptyAndAllPassed_ReturnsTrue()
    {
        // Arrange
        var alice = _players[0];
        _priorityManager.InitializeForPhase(alice);
        _priorityManager.PassPriority();
        _priorityManager.PassPriority();

        // Act
        var result = _priorityManager.CanEndPhase();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanEndPhase_StackNotEmpty_ReturnsFalse()
    {
        // Arrange
        var alice = _players[0];
        var card = new Instant("Lightning Bolt", "R") { Owner = alice };
        var spell = new Spell(card, alice);
        _stack.Push(spell);
        _priorityManager.InitializeForPhase(alice);
        _priorityManager.PassPriority();
        _priorityManager.PassPriority();

        // Act
        var result = _priorityManager.CanEndPhase();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Reset_ResetsState()
    {
        // Arrange
        var alice = _players[0];
        _priorityManager.InitializeForPhase(alice);
        _priorityManager.PassPriority();

        // Act
        _priorityManager.Reset();

        // Assert
        _priorityManager.CurrentPlayer.Should().BeNull();
        _priorityManager.AllPlayersPassed.Should().BeFalse();
    }
}
