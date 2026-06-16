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
    public void CanAttack_Defender_ReturnsFalse()
    {
        // CR 702.3b — a creature with defender can't attack.
        var player = new Player("Alice", 20);
        var wall = new Creature("Wall", "1G", 0, 4) { Owner = player, Controller = player };
        wall.SetZone(ZoneType.Battlefield);
        wall.HasSummoningSickness = false;
        wall.AddAbility(new Majik.Core.Abilities.KeywordAbility("Defender", wall, player));

        _validator.CanAttack(wall, player).Should().BeFalse();
    }

    [Fact]
    public void CanAttack_Defender_WithAttackAsThoughNoDefenderGrant_ReturnsTrue()
    {
        // CR 508.1a relaxation (Nivix Cyclops) — the per-turn grant permits a
        // defender creature to attack.
        var player = new Player("Alice", 20);
        var wall = new Creature("Wall", "1G", 0, 4) { Owner = player, Controller = player };
        wall.SetZone(ZoneType.Battlefield);
        wall.HasSummoningSickness = false;
        wall.AddAbility(new Majik.Core.Abilities.KeywordAbility("Defender", wall, player));
        wall.CanAttackAsThoughItDidntHaveDefenderThisTurn = true;

        _validator.CanAttack(wall, player).Should().BeTrue();
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
    public void CanBlock_IntrinsicCantBlock_ReturnsFalse()
    {
        // CR 509.1a — a creature with an intrinsic "can't block" restriction
        // (e.g. Mirrex's Phyrexian Mite token's quoted "This token can't block.",
        // recorded as a "CantBlock" KeywordAbility) can't be declared as a blocker.
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = attackingPlayer };
        var blockerCreature = new Creature("Phyrexian Mite", "", 1, 1) { Controller = defendingPlayer };
        blockerCreature.SetZone(ZoneType.Battlefield);
        blockerCreature.AddAbility(new Majik.Core.Abilities.KeywordAbility(
            "CantBlock", blockerCreature, defendingPlayer));
        var attacker = new Attacker(attackerCreature, defendingPlayer);

        _validator.CanBlock(blockerCreature, attacker, defendingPlayer).Should().BeFalse();
    }

    [Fact]
    public void CanBlock_CanBlockOnlyFlying_NonFlyingAttacker_ReturnsFalse()
    {
        // CR 509.1b — "This creature can block only creatures with flying"
        // (Brazen Borrower, Shacklegeist, Pinnacle Emissary's Drone token). The
        // blocker may be declared ONLY against a flying attacker; a non-flying
        // attacker can't be blocked by it.
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var groundAttacker = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = attackingPlayer };
        var blocker = new Creature("Brazen Borrower", "1UU", 3, 1) { Controller = defendingPlayer };
        blocker.SetZone(ZoneType.Battlefield);
        blocker.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", blocker, defendingPlayer));
        blocker.AddAbility(new Majik.Core.Abilities.KeywordAbility("CanBlockOnlyFlying", blocker, defendingPlayer));
        var attacker = new Attacker(groundAttacker, defendingPlayer);

        _validator.CanBlock(blocker, attacker, defendingPlayer).Should().BeFalse();
    }

    [Fact]
    public void CanBlock_CanBlockOnlyFlying_FlyingAttacker_ReturnsTrue()
    {
        // CR 509.1b — the same blocker MAY block a flying attacker.
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var flyingAttacker = new Creature("Drake", "2U", 2, 2) { Controller = attackingPlayer };
        flyingAttacker.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", flyingAttacker, attackingPlayer));
        var blocker = new Creature("Brazen Borrower", "1UU", 3, 1) { Controller = defendingPlayer };
        blocker.SetZone(ZoneType.Battlefield);
        blocker.AddAbility(new Majik.Core.Abilities.KeywordAbility("Flying", blocker, defendingPlayer));
        blocker.AddAbility(new Majik.Core.Abilities.KeywordAbility("CanBlockOnlyFlying", blocker, defendingPlayer));
        var attacker = new Attacker(flyingAttacker, defendingPlayer);

        _validator.CanBlock(blocker, attacker, defendingPlayer).Should().BeTrue();
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
