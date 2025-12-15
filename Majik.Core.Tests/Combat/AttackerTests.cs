using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Unit tests for Attacker entity.
/// Tests attacker creation, damage assignment, and combat abilities.
/// </summary>
public class AttackerTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesAttacker()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);

        // Act
        var attacker = new Attacker(creature, targetPlayer);

        // Assert
        attacker.Creature.Should().Be(creature);
        attacker.TargetPlayer.Should().Be(targetPlayer);
        attacker.TargetPlaneswalker.Should().BeNull();
        attacker.Blockers.Should().BeEmpty();
        attacker.AssignedDamage.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithCombatAbilities_SetsAbilities()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);

        // Act
        var attacker = new Attacker(creature, targetPlayer, 
            hasFirstStrike: true, hasTrample: true, hasDeathtouch: true);

        // Assert
        attacker.HasFirstStrike.Should().BeTrue();
        attacker.HasTrample.Should().BeTrue();
        attacker.HasDeathtouch.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullCreature_ThrowsException()
    {
        // Arrange
        var targetPlayer = new Player("Bob", 20);

        // Act & Assert
        new Action(() => new Attacker(null!, targetPlayer))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NoTarget_ThrowsException()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act & Assert
        new Action(() => new Attacker(creature, null, null))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssignDamage_ValidAmount_IncreasesDamage()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer);

        // Act
        attacker.AssignDamage(2);

        // Assert
        attacker.AssignedDamage.Should().Be(2);
    }

    [Fact]
    public void AssignDamage_NegativeAmount_ThrowsException()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer);

        // Act & Assert
        attacker.Invoking(a => a.AssignDamage(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResetDamageAssignment_ResetsDamage()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer);
        attacker.AssignDamage(2);

        // Act
        attacker.ResetDamageAssignment();

        // Assert
        attacker.AssignedDamage.Should().Be(0);
    }

    [Fact]
    public void GetPower_ReturnsCreaturePower()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer);

        // Act
        var power = attacker.GetPower();

        // Assert
        power.Should().Be(2);
    }

    [Fact]
    public void CanDealFirstStrikeDamage_WithFirstStrike_ReturnsTrue()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer, hasFirstStrike: true);

        // Act & Assert
        attacker.CanDealFirstStrikeDamage().Should().BeTrue();
    }

    [Fact]
    public void CanDealFirstStrikeDamage_WithDoubleStrike_ReturnsTrue()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer, hasDoubleStrike: true);

        // Act & Assert
        attacker.CanDealFirstStrikeDamage().Should().BeTrue();
    }

    [Fact]
    public void CanDealRegularDamage_WithoutFirstStrike_ReturnsTrue()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer);

        // Act & Assert
        attacker.CanDealRegularDamage().Should().BeTrue();
    }

    [Fact]
    public void CanDealRegularDamage_WithDoubleStrike_ReturnsTrue()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer, hasDoubleStrike: true);

        // Act & Assert
        attacker.CanDealRegularDamage().Should().BeTrue();
    }

    [Fact]
    public void CanDealRegularDamage_WithOnlyFirstStrike_ReturnsFalse()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(creature, targetPlayer, hasFirstStrike: true);

        // Act & Assert
        attacker.CanDealRegularDamage().Should().BeFalse();
    }
}
