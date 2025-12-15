using FluentAssertions;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Domain.Aggregates;

/// <summary>
/// Unit tests for Game aggregate root.
/// Tests game initialization, player management, and invariants.
/// </summary>
public class GameTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesGame()
    {
        // Act
        var game = new Majik.Core.Domain.Aggregates.Game();

        // Assert
        game.Players.Should().BeEmpty();
        game.IsStarted.Should().BeFalse();
        game.ActivePlayer.Should().BeNull();
        game.TurnNumber.Should().Be(0);
    }

    [Fact]
    public void AddPlayer_ValidPlayer_AddsToGame()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();

        // Act
        game.AddPlayer("Alice", 20);

        // Assert
        game.Players.Should().HaveCount(1);
        game.Players[0].Name.Should().Be("Alice");
        game.Players[0].LifeTotal.Should().Be(20);
    }

    [Fact]
    public void AddPlayer_MultiplePlayers_AddsAll()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();

        // Act
        game.AddPlayer("Alice", 20);
        game.AddPlayer("Bob", 20);

        // Assert
        game.Players.Should().HaveCount(2);
    }

    [Fact]
    public void StartGame_LessThanTwoPlayers_ThrowsException()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();
        game.AddPlayer("Alice", 20);

        // Act & Assert
        new Action(() => game.StartGame())
            .Should().Throw<InvalidGameStateException>();
    }

    [Fact]
    public void StartGame_TwoPlayers_StartsGame()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();
        game.AddPlayer("Alice", 20);
        game.AddPlayer("Bob", 20);

        // Act
        game.StartGame();

        // Assert
        game.IsStarted.Should().BeTrue();
        game.ActivePlayer.Should().NotBeNull();
        game.TurnNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StartGame_AlreadyStarted_ThrowsException()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();
        game.AddPlayer("Alice", 20);
        game.AddPlayer("Bob", 20);
        game.StartGame();

        // Act & Assert
        new Action(() => game.StartGame())
            .Should().Throw<InvalidGameStateException>();
    }

    [Fact]
    public void GetPlayer_ExistingPlayer_ReturnsPlayer()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();
        game.AddPlayer("Alice", 20);

        // Act
        var player = game.GetPlayer("Alice");

        // Assert
        player.Should().NotBeNull();
        player!.Name.Should().Be("Alice");
    }

    [Fact]
    public void GetPlayer_NonExistentPlayer_ReturnsNull()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();
        game.AddPlayer("Alice", 20);

        // Act
        var player = game.GetPlayer("Bob");

        // Assert
        player.Should().BeNull();
    }

    [Fact]
    public void Stack_AfterStartGame_IsInitialized()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();
        game.AddPlayer("Alice", 20);
        game.AddPlayer("Bob", 20);

        // Act
        game.StartGame();

        // Assert
        game.Stack.Should().NotBeNull();
        game.Stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void PriorityManager_AfterStartGame_IsInitialized()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();
        game.AddPlayer("Alice", 20);
        game.AddPlayer("Bob", 20);

        // Act
        game.StartGame();

        // Assert
        game.PriorityManager.Should().NotBeNull();
    }

    [Fact]
    public void Stack_BeforeStartGame_ThrowsException()
    {
        // Arrange
        var game = new Majik.Core.Domain.Aggregates.Game();

        // Act & Assert
        new Action(() => { var _ = game.Stack; })
            .Should().Throw<InvalidGameStateException>();
    }
}
