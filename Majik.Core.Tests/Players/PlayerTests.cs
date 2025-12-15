using FluentAssertions;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Players;

/// <summary>
/// Unit tests for Player entity.
/// Tests life management, mana pool, and game loss.
/// </summary>
public class PlayerTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesPlayer()
    {
        // Act
        var player = new Player("Alice", 20);

        // Assert
        player.Name.Should().Be("Alice");
        player.LifeTotal.Should().Be(20);
        player.HasLost.Should().BeFalse();
        player.ManaPool.IsEmpty.Should().BeTrue();
        player.Zones.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NullName_ThrowsException()
    {
        // Act & Assert
        new Action(() => new Player(null!, 20))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyName_ThrowsException()
    {
        // Act & Assert
        new Action(() => new Player("", 20))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhitespaceName_ThrowsException()
    {
        // Act & Assert
        new Action(() => new Player("   ", 20))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GainLife_ValidAmount_IncreasesLifeTotal()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        player.GainLife(5);

        // Assert
        player.LifeTotal.Should().Be(25);
    }

    [Fact]
    public void GainLife_NegativeAmount_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act & Assert
        player.Invoking(p => p.GainLife(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GainLife_AfterLosing_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.LoseLife(20);
        player.HasLost.Should().BeTrue();

        // Act & Assert
        player.Invoking(p => p.GainLife(5))
            .Should().Throw<InvalidPlayerActionException>()
            .WithMessage("*Cannot gain life after losing*");
    }

    [Fact]
    public void LoseLife_ValidAmount_DecreasesLifeTotal()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        player.LoseLife(5);

        // Assert
        player.LifeTotal.Should().Be(15);
        player.HasLost.Should().BeFalse();
    }

    [Fact]
    public void LoseLife_ReducesToZero_SetsHasLost()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        player.LoseLife(20);

        // Assert
        player.LifeTotal.Should().Be(0);
        player.HasLost.Should().BeTrue();
    }

    [Fact]
    public void LoseLife_ReducesBelowZero_SetsHasLost()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        player.LoseLife(25);

        // Assert
        player.LifeTotal.Should().Be(-5);
        player.HasLost.Should().BeTrue();
    }

    [Fact]
    public void LoseLife_NegativeAmount_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act & Assert
        player.Invoking(p => p.LoseLife(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LoseLife_AfterLosing_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.LoseLife(20);

        // Act & Assert
        player.Invoking(p => p.LoseLife(5))
            .Should().Throw<InvalidPlayerActionException>()
            .WithMessage("*Cannot lose life after losing*");
    }

    [Fact]
    public void AddManaToPool_ValidMana_UpdatesPool()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var mana = ManaCost.Parse("RR");

        // Act
        player.AddManaToPool(mana);

        // Assert
        player.ManaPool.Red.Should().Be(2);
        player.ManaPool.Total.Should().Be(2);
    }

    [Fact]
    public void AddManaToPool_NullMana_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act & Assert
        player.Invoking(p => p.AddManaToPool(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddManaToPool_AfterLosing_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.LoseLife(20);

        // Act & Assert
        player.Invoking(p => p.AddManaToPool(ManaCost.Parse("R")))
            .Should().Throw<InvalidPlayerActionException>()
            .WithMessage("*Cannot add mana after losing*");
    }

    [Fact]
    public void PayMana_SufficientMana_ReturnsTrueAndUpdatesPool()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("RR"));
        var cost = ManaCost.Parse("R");

        // Act
        var result = player.PayMana(cost);

        // Assert
        result.Should().BeTrue();
        player.ManaPool.Red.Should().Be(1);
    }

    [Fact]
    public void PayMana_InsufficientMana_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));
        var cost = ManaCost.Parse("RR");

        // Act
        var result = player.PayMana(cost);

        // Assert
        result.Should().BeFalse();
        player.ManaPool.Red.Should().Be(1); // Unchanged
    }

    [Fact]
    public void PayMana_NullCost_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act & Assert
        player.Invoking(p => p.PayMana(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PayMana_AfterLosing_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));
        player.LoseLife(20);
        var cost = ManaCost.Parse("R");

        // Act
        var result = player.PayMana(cost);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EmptyManaPool_EmptiesPool()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("3RR"));

        // Act
        player.EmptyManaPool();

        // Assert
        player.ManaPool.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));

        // Act
        var result = player.ToString();

        // Assert
        result.Should().Contain("Alice");
        result.Should().Contain("20");
        result.Should().Contain("mana");
    }
}
