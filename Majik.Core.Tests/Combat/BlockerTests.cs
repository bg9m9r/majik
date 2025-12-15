using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Unit tests for Blocker entity.
/// Tests blocker creation, damage assignment, and combat abilities.
/// </summary>
public class BlockerTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesBlocker()
    {
        // Arrange
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(attackerCreature, targetPlayer);

        // Act
        var blocker = new Blocker(blockerCreature, attacker);

        // Assert
        blocker.Creature.Should().Be(blockerCreature);
        blocker.BlockedAttacker.Should().Be(attacker);
        blocker.AssignedDamage.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithCombatAbilities_SetsAbilities()
    {
        // Arrange
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(attackerCreature, targetPlayer);

        // Act
        var blocker = new Blocker(blockerCreature, attacker, 
            hasFirstStrike: true, hasDeathtouch: true);

        // Assert
        blocker.HasFirstStrike.Should().BeTrue();
        blocker.HasDeathtouch.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullCreature_ThrowsException()
    {
        // Arrange
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(attackerCreature, targetPlayer);

        // Act & Assert
        new Action(() => new Blocker(null!, attacker))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullAttacker_ThrowsException()
    {
        // Arrange
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1);

        // Act & Assert
        new Action(() => new Blocker(blockerCreature, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AssignDamage_ValidAmount_IncreasesDamage()
    {
        // Arrange
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(attackerCreature, targetPlayer);
        var blocker = new Blocker(blockerCreature, attacker);

        // Act
        blocker.AssignDamage(1);

        // Assert
        blocker.AssignedDamage.Should().Be(1);
    }

    [Fact]
    public void AssignDamage_NegativeAmount_ThrowsException()
    {
        // Arrange
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(attackerCreature, targetPlayer);
        var blocker = new Blocker(blockerCreature, attacker);

        // Act & Assert
        blocker.Invoking(b => b.AssignDamage(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResetDamageAssignment_ResetsDamage()
    {
        // Arrange
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(attackerCreature, targetPlayer);
        var blocker = new Blocker(blockerCreature, attacker);
        blocker.AssignDamage(1);

        // Act
        blocker.ResetDamageAssignment();

        // Assert
        blocker.AssignedDamage.Should().Be(0);
    }

    [Fact]
    public void GetPower_ReturnsCreaturePower()
    {
        // Arrange
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2);
        var targetPlayer = new Player("Bob", 20);
        var attacker = new Attacker(attackerCreature, targetPlayer);
        var blocker = new Blocker(blockerCreature, attacker);

        // Act
        var power = blocker.GetPower();

        // Assert
        power.Should().Be(1);
    }
}
