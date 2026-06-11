using System.Collections.Generic;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Rules;
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
        // Arrange — a player who has FORMALLY lost (marked by the SBA loop,
        // i.e. left the game) must reject life gain. CR 704.5a — loss is the
        // SBA, so the "has lost" state comes from MarkLost(), not from
        // LoseLife's life arithmetic.
        var player = new Player("Alice", 20);
        player.MarkLost();
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
    public void LoseLife_ReducesToZero_DoesNotSetHasLostUntilSba()
    {
        // CR 704.5a — losing the game for 0-or-less life is a STATE-BASED
        // action. Reducing life to 0 does NOT instantaneously set HasLost;
        // the player only loses when SBAs are next checked. (Previously this
        // test asserted HasLost == true here, which encoded the eager-flip
        // bug that crashed mid-payment life costs.)
        var player = new Player("Alice", 20);

        // Act
        player.LoseLife(20);

        // Assert
        player.LifeTotal.Should().Be(0);
        player.HasLost.Should().BeFalse();
    }

    [Fact]
    public void LoseLife_ReducesBelowZero_DoesNotSetHasLostUntilSba()
    {
        // CR 704.5a — see above; below-zero life likewise loses only at the
        // next SBA check, not eagerly inside LoseLife.
        var player = new Player("Alice", 20);

        // Act
        player.LoseLife(25);

        // Assert
        player.LifeTotal.Should().Be(-5);
        player.HasLost.Should().BeFalse();
    }

    [Fact]
    public void PayingLifeAsCost_AtZero_DoesNotBlockSamePaymentsMana_LossDeferredToSba()
    {
        // CR 104.3a / 119.4 / 704.5a — repro of the fuzz-harness crash:
        // a pain/Horizon land's mana ability pays 1 life as part of a mana
        // payment ("{T}, Pay 1 life: Add {R}/{W}"). Paying the player to 0
        // must NOT end the game mid-payment; the cast completes and the
        // player loses only at the next SBA check.
        var player = new Player("Alice", 1);

        // Pay 1 life as a cost — player is now at 0 life.
        player.LoseLife(1);

        // Immediately after the life payment the player has NOT yet lost
        // (loss is a state-based action that has not run yet)...
        player.LifeTotal.Should().Be(0);
        player.HasLost.Should().BeFalse();

        // ...so the rest of the SAME payment can still add mana to the pool.
        player.Invoking(p => p.AddManaToPool(ManaCost.Parse("R")))
            .Should().NotThrow();
        player.ManaPool.Red.Should().Be(1);

        // Once SBAs are checked, the 0-life player formally loses (CR 704.5a).
        var sba = new StateBasedActions();
        sba.CheckStateBasedActions(new List<Player> { player }, new List<ICard>());
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
        // Arrange — a player who has FORMALLY lost (left the game via the SBA)
        // rejects further life loss. The "has lost" state is established by
        // MarkLost(), not by LoseLife's arithmetic (CR 704.5a).
        var player = new Player("Alice", 20);
        player.MarkLost();

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
        // Arrange — guard for a player who has FORMALLY lost (left the game).
        // Establish that state via MarkLost(), not via LoseLife's arithmetic:
        // CR 704.5a makes loss a state-based action, and a mid-payment life
        // cost that drops a player to 0 must NOT block the AddManaToPool that
        // finishes the same payment (that was the fuzz-harness crash).
        var player = new Player("Alice", 20);
        player.MarkLost();

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
        // Arrange — guard for a player who has FORMALLY lost (CR 704.5a);
        // establish via MarkLost(), not LoseLife's arithmetic.
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));
        player.MarkLost();
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
