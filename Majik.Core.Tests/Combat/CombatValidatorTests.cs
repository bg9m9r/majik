using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Unit tests for CombatValidator service.
/// Tests validation of attack and block declarations.
/// </summary>
public class CombatValidatorTests
{
    private readonly CombatValidator _validator;

    public CombatValidatorTests()
    {
        _validator = new CombatValidator();
    }

    [Fact]
    public void CanAttack_ValidCreature_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        creature.SetZone(ZoneType.Battlefield);
        creature.HasSummoningSickness = false;

        // Act
        var result = _validator.CanAttack(creature, player);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanAttack_NullCreature_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        var result = _validator.CanAttack(null!, player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttack_NullPlayer_ReturnsFalse()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act
        var result = _validator.CanAttack(creature, null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttack_WrongController_ReturnsFalse()
    {
        // Arrange
        var player1 = new Player("Alice", 20);
        var player2 = new Player("Bob", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player1 };
        creature.SetZone(ZoneType.Battlefield);

        // Act
        var result = _validator.CanAttack(creature, player2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttack_NotOnBattlefield_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        creature.SetZone(ZoneType.Hand);

        // Act
        var result = _validator.CanAttack(creature, player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttack_TappedCreature_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        creature.SetZone(ZoneType.Battlefield);
        creature.Tap();
        creature.HasSummoningSickness = false;

        // Act
        var result = _validator.CanAttack(creature, player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttack_SummoningSickness_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        creature.SetZone(ZoneType.Battlefield);
        creature.HasSummoningSickness = true;

        // Act
        var result = _validator.CanAttack(creature, player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBlock_ValidBlock_ReturnsTrue()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = attackingPlayer };
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = defendingPlayer };
        blockerCreature.SetZone(ZoneType.Battlefield);
        var attacker = new Attacker(attackerCreature, defendingPlayer);

        // Act
        var result = _validator.CanBlock(blockerCreature, attacker, defendingPlayer);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanBlock_NullCreature_ReturnsFalse()
    {
        // Arrange
        var defendingPlayer = new Player("Bob", 20);
        var attacker = new Attacker(new Creature("Grizzly Bears", "1G", 2, 2), defendingPlayer);

        // Act
        var result = _validator.CanBlock(null!, attacker, defendingPlayer);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBlock_WrongController_ReturnsFalse()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = attackingPlayer };
        blockerCreature.SetZone(ZoneType.Battlefield);
        var attacker = new Attacker(new Creature("Grizzly Bears", "1G", 2, 2), defendingPlayer);

        // Act
        var result = _validator.CanBlock(blockerCreature, attacker, defendingPlayer);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBlock_TappedCreature_ReturnsFalse()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = defendingPlayer };
        blockerCreature.SetZone(ZoneType.Battlefield);
        blockerCreature.Tap();
        var attacker = new Attacker(new Creature("Grizzly Bears", "1G", 2, 2), defendingPlayer);

        // Act
        var result = _validator.CanBlock(blockerCreature, attacker, defendingPlayer);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttackPlayer_ValidTarget_ReturnsTrue()
    {
        // Arrange
        var attacker = new Player("Alice", 20);
        var target = new Player("Bob", 20);

        // Act
        var result = _validator.CanAttackPlayer(target, attacker);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanAttackPlayer_Self_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        var result = _validator.CanAttackPlayer(player, player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttackPlayer_TargetHasLost_ReturnsFalse()
    {
        // Arrange
        var attacker = new Player("Alice", 20);
        var target = new Player("Bob", 20);
        target.HasLost = true;

        // Act
        var result = _validator.CanAttackPlayer(target, attacker);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttackPlaneswalker_ValidTarget_ReturnsTrue()
    {
        // Arrange
        var attacker = new Player("Alice", 20);
        var owner = new Player("Bob", 20);
        var planeswalker = new Planeswalker("Jace", "2UU", 3) { Controller = owner };
        planeswalker.SetZone(ZoneType.Battlefield);

        // Act
        var result = _validator.CanAttackPlaneswalker(planeswalker, attacker);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanAttackPlaneswalker_OwnPlaneswalker_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var planeswalker = new Planeswalker("Jace", "2UU", 3) { Controller = player };
        planeswalker.SetZone(ZoneType.Battlefield);

        // Act
        var result = _validator.CanAttackPlaneswalker(planeswalker, player);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanAttackPlaneswalker_DeadPlaneswalker_ReturnsFalse()
    {
        // Arrange
        var attacker = new Player("Alice", 20);
        var owner = new Player("Bob", 20);
        var planeswalker = new Planeswalker("Jace", "2UU", 3) { Controller = owner };
        planeswalker.SetZone(ZoneType.Battlefield);
        planeswalker.RemoveLoyalty(3);

        // Act
        var result = _validator.CanAttackPlaneswalker(planeswalker, attacker);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidAttackDeclaration_ValidAttackers_ReturnsTrue()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var creature1 = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        var creature2 = new Creature("Lightning Bolt", "R", 1, 1) { Controller = activePlayer };
        creature1.SetZone(ZoneType.Battlefield);
        creature2.SetZone(ZoneType.Battlefield);
        creature1.HasSummoningSickness = false;
        creature2.HasSummoningSickness = false;
        var attackers = new[] { creature1, creature2 };

        // Act
        var result = _validator.IsValidAttackDeclaration(attackers, activePlayer, targetPlayer, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidAttackDeclaration_NoTarget_ReturnsFalse()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        creature.SetZone(ZoneType.Battlefield);
        var attackers = new[] { creature };

        // Act
        var result = _validator.IsValidAttackDeclaration(attackers, activePlayer, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidBlockDeclaration_ValidBlocks_ReturnsTrue()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = attackingPlayer };
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = defendingPlayer };
        blockerCreature.SetZone(ZoneType.Battlefield);
        var attacker = new Attacker(attackerCreature, defendingPlayer);
        var blocks = new[] { (blockerCreature, attacker) };

        // Act
        var result = _validator.IsValidBlockDeclaration(blocks, defendingPlayer);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidBlockDeclaration_DuplicateBlocker_ReturnsFalse()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var attackerCreature1 = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = attackingPlayer };
        var attackerCreature2 = new Creature("Lightning Bolt", "R", 1, 1) { Controller = attackingPlayer };
        var blockerCreature = new Creature("Block", "1", 1, 1) { Controller = defendingPlayer };
        blockerCreature.SetZone(ZoneType.Battlefield);
        var attacker1 = new Attacker(attackerCreature1, defendingPlayer);
        var attacker2 = new Attacker(attackerCreature2, defendingPlayer);
        var blocks = new[] { (blockerCreature, attacker1), (blockerCreature, attacker2) };

        // Act
        var result = _validator.IsValidBlockDeclaration(blocks, defendingPlayer);

        // Assert
        result.Should().BeFalse();
    }
}
