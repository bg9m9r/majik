using FluentAssertions;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Domain.ValueObjects;

/// <summary>
/// Unit tests for PriorityState value object.
/// Tests state creation, transitions, and pass counting.
/// </summary>
public class PriorityStateTests
{
    private Player CreatePlayer(string name) => new Player(name, 20);

    [Fact]
    public void Initial_ValidInput_CreatesInitialState()
    {
        // Arrange
        var player = CreatePlayer("Alice");

        // Act
        var state = PriorityState.Initial(player, 2);

        // Assert
        state.CurrentPlayer.Should().Be(player);
        state.ActivePlayer.Should().Be(player);
        state.PassCount.Should().Be(0);
        state.TotalPlayers.Should().Be(2);
        state.AllPlayersPassed.Should().BeFalse();
    }

    [Fact]
    public void Initial_NullPlayer_ThrowsException()
    {
        // Act & Assert
        new Action(() => PriorityState.Initial(null!, 2))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Initial_LessThanTwoPlayers_ThrowsException()
    {
        // Arrange
        var player = CreatePlayer("Alice");

        // Act & Assert
        new Action(() => PriorityState.Initial(player, 1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reset_ValidInput_CreatesResetState()
    {
        // Act
        var state = PriorityState.Reset(2);

        // Assert
        state.CurrentPlayer.Should().BeNull();
        state.ActivePlayer.Should().BeNull();
        state.PassCount.Should().Be(0);
        state.TotalPlayers.Should().Be(2);
    }

    [Fact]
    public void WithCurrentPlayer_ChangesCurrentPlayer_ResetsPassCount()
    {
        // Arrange
        var alice = CreatePlayer("Alice");
        var bob = CreatePlayer("Bob");
        var state = PriorityState.Initial(alice, 2).WithPassIncremented();

        // Act
        var result = state.WithCurrentPlayer(bob);

        // Assert
        result.CurrentPlayer.Should().Be(bob);
        result.PassCount.Should().Be(0); // Reset
        result.ActivePlayer.Should().Be(alice);
    }

    [Fact]
    public void WithCurrentPlayerKeepPassCount_ChangesCurrentPlayer_KeepsPassCount()
    {
        // Arrange
        var alice = CreatePlayer("Alice");
        var bob = CreatePlayer("Bob");
        var state = PriorityState.Initial(alice, 2).WithPassIncremented();

        // Act
        var result = state.WithCurrentPlayerKeepPassCount(bob);

        // Assert
        result.CurrentPlayer.Should().Be(bob);
        result.PassCount.Should().Be(1); // Kept
        result.ActivePlayer.Should().Be(alice);
    }

    [Fact]
    public void WithPassIncremented_IncrementsPassCount()
    {
        // Arrange
        var player = CreatePlayer("Alice");
        var state = PriorityState.Initial(player, 2);

        // Act
        var result = state.WithPassIncremented();

        // Assert
        result.PassCount.Should().Be(1);
        result.CurrentPlayer.Should().Be(player);
    }

    [Fact]
    public void WithPassReset_ResetsPassCount()
    {
        // Arrange
        var player = CreatePlayer("Alice");
        var state = PriorityState.Initial(player, 2).WithPassIncremented().WithPassIncremented();

        // Act
        var result = state.WithPassReset();

        // Assert
        result.PassCount.Should().Be(0);
    }

    [Fact]
    public void AllPlayersPassed_WhenPassCountEqualsTotalPlayers_ReturnsTrue()
    {
        // Arrange
        var player = CreatePlayer("Alice");
        var state = PriorityState.Initial(player, 2)
            .WithPassIncremented()
            .WithPassIncremented();

        // Act & Assert
        state.AllPlayersPassed.Should().BeTrue();
    }

    [Fact]
    public void AllPlayersPassed_WhenPassCountLessThanTotalPlayers_ReturnsFalse()
    {
        // Arrange
        var player = CreatePlayer("Alice");
        var state = PriorityState.Initial(player, 2).WithPassIncremented();

        // Act & Assert
        state.AllPlayersPassed.Should().BeFalse();
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Arrange
        var alice = CreatePlayer("Alice");
        var state1 = PriorityState.Initial(alice, 2);
        var state2 = PriorityState.Initial(alice, 2);

        // Act & Assert
        state1.Equals(state2).Should().BeTrue();
        (state1 == state2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        // Arrange
        var alice = CreatePlayer("Alice");
        var bob = CreatePlayer("Bob");
        var state1 = PriorityState.Initial(alice, 2);
        var state2 = PriorityState.Initial(bob, 2);

        // Act & Assert
        state1.Equals(state2).Should().BeFalse();
        (state1 != state2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHashCode()
    {
        // Arrange
        var alice = CreatePlayer("Alice");
        var state1 = PriorityState.Initial(alice, 2);
        var state2 = PriorityState.Initial(alice, 2);

        // Act & Assert
        state1.GetHashCode().Should().Be(state2.GetHashCode());
    }
}
