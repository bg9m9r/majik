using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;
using Attacker = Majik.Core.Combat.Attacker;
using Blocker = Majik.Core.Combat.Blocker;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Unit tests for Combat entity.
/// Tests combat creation, state transitions, and attacker management.
/// </summary>
public class CombatTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesCombat()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);

        // Act
        var combat = new Majik.Core.Combat.Combat(attackingPlayer, defendingPlayer);

        // Assert
        combat.AttackingPlayer.Should().Be(attackingPlayer);
        combat.DefendingPlayer.Should().Be(defendingPlayer);
        combat.TargetPlaneswalker.Should().BeNull();
        combat.Attackers.Should().BeEmpty();
        combat.State.Should().Be(Majik.Core.Combat.CombatState.DeclaringAttackers);
        combat.IsEnded.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithPlaneswalker_CreatesCombat()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var planeswalker = new Planeswalker("Jace", "2UU", 3);

        // Act
        var combat = new Majik.Core.Combat.Combat(attackingPlayer, null, planeswalker);

        // Assert
        combat.AttackingPlayer.Should().Be(attackingPlayer);
        combat.DefendingPlayer.Should().BeNull();
        combat.TargetPlaneswalker.Should().Be(planeswalker);
    }

    [Fact]
    public void Constructor_NullAttackingPlayer_ThrowsException()
    {
        // Arrange
        var defendingPlayer = new Player("Bob", 20);

        // Act & Assert
        new Action(() => new Majik.Core.Combat.Combat(null!, defendingPlayer))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NoTarget_ThrowsException()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);

        // Act & Assert
        new Action(() => new Majik.Core.Combat.Combat(attackingPlayer, null, null))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddAttacker_ValidAttacker_AddsToCombat()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var combat = new Majik.Core.Combat.Combat(attackingPlayer, defendingPlayer);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var attacker = new Attacker(creature, defendingPlayer);

        // Act
        combat.AddAttacker(attacker);

        // Assert
        combat.Attackers.Should().HaveCount(1);
        combat.Attackers[0].Should().Be(attacker);
    }

    [Fact]
    public void AddAttacker_WrongState_ThrowsException()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var combat = new Majik.Core.Combat.Combat(attackingPlayer, defendingPlayer);
        combat.TransitionToDeclaringBlockers();
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var attacker = new Attacker(creature, defendingPlayer);

        // Act & Assert
        combat.Invoking(c => c.AddAttacker(attacker))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TransitionToDeclaringBlockers_FromDeclaringAttackers_Transitions()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var combat = new Majik.Core.Combat.Combat(attackingPlayer, defendingPlayer);

        // Act
        combat.TransitionToDeclaringBlockers();

        // Assert
        combat.State.Should().Be(Majik.Core.Combat.CombatState.DeclaringBlockers);
    }

    [Fact]
    public void TransitionToDeclaringBlockers_WrongState_ThrowsException()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var combat = new Majik.Core.Combat.Combat(attackingPlayer, defendingPlayer);
        combat.TransitionToDeclaringBlockers();
        combat.TransitionToAssigningDamage();

        // Act & Assert
        combat.Invoking(c => c.TransitionToDeclaringBlockers())
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void End_EndsCombat()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var combat = new Majik.Core.Combat.Combat(attackingPlayer, defendingPlayer);

        // Act
        combat.End();

        // Assert
        combat.State.Should().Be(Majik.Core.Combat.CombatState.Resolved);
        combat.IsEnded.Should().BeTrue();
    }

    [Fact]
    public void GetAllBlockers_ReturnsAllBlockers()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var combat = new Majik.Core.Combat.Combat(attackingPlayer, defendingPlayer);
        var attacker1 = new Attacker(new Creature("Grizzly Bears", "1G", 2, 2), defendingPlayer);
        var attacker2 = new Attacker(new Creature("Lightning Bolt", "R", 1, 1), defendingPlayer);
        combat.AddAttacker(attacker1);
        combat.AddAttacker(attacker2);
        var blocker1 = new Blocker(new Creature("Block1", "1", 1, 1), attacker1);
        var blocker2 = new Blocker(new Creature("Block2", "1", 1, 1), attacker2);
        attacker1.AddBlocker(blocker1);
        attacker2.AddBlocker(blocker2);

        // Act
        var blockers = combat.GetAllBlockers();

        // Assert
        blockers.Should().HaveCount(2);
        blockers.Should().Contain(blocker1);
        blockers.Should().Contain(blocker2);
    }
}
