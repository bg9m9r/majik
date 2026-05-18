using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Unit tests for CombatManager service.
/// Tests combat flow, damage assignment, and event publishing.
/// </summary>
public class CombatManagerTests
{
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly StateBasedActions _stateBasedActions;
    private readonly ZoneService _zoneService;
    private readonly CombatManager _combatManager;

    public CombatManagerTests()
    {
        _eventBusMock = new Mock<IEventBus>();
        _stateBasedActions = new StateBasedActions(_eventBusMock.Object);
        _zoneService = new ZoneService(_eventBusMock.Object);
        _combatManager = new CombatManager(_eventBusMock.Object, _stateBasedActions, _zoneService);
    }

    [Fact]
    public void StartCombat_ValidPlayer_StartsCombat()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        _combatManager.StartCombat(player);

        // Assert
        _combatManager.IsInCombat.Should().BeFalse(); // No attackers declared yet
        _eventBusMock.Verify(x => x.Publish(It.IsAny<CombatStartedEvent>()), Times.Once);
    }

    [Fact]
    public void StartCombat_NullPlayer_ThrowsException()
    {
        // Act & Assert
        _combatManager.Invoking(c => c.StartCombat(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StartCombat_AlreadyInCombat_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = player };
        creature.Zone = ZoneType.Battlefield;
        creature.HasSummoningSickness = false;
        _combatManager.StartCombat(player);
        _combatManager.DeclareAttackers(player, new[]
        {
            new AttackerDeclaration(creature, targetPlayer)
        });

        // Act & Assert
        _combatManager.Invoking(c => c.StartCombat(player))
            .Should().Throw<InvalidGameStateException>();
    }

    [Fact]
    public void DeclareAttackers_ValidAttackers_CreatesCombat()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        creature.Zone = ZoneType.Battlefield;
        creature.HasSummoningSickness = false;
        _combatManager.StartCombat(activePlayer);

        // Act
        _combatManager.DeclareAttackers(activePlayer, new[]
        {
            new AttackerDeclaration(creature, targetPlayer)
        });

        // Assert
        _combatManager.IsInCombat.Should().BeTrue();
        _combatManager.CurrentCombat.Should().NotBeNull();
        _combatManager.CurrentCombat!.Attackers.Should().HaveCount(1);
        creature.IsTapped.Should().BeTrue(); // Attacker should be tapped
        _eventBusMock.Verify(x => x.Publish(It.IsAny<AttackersDeclaredEvent>()), Times.Once);
    }

    [Fact]
    public void DeclareAttackers_InvalidAttacker_ThrowsException()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        creature.Zone = ZoneType.Hand; // Not on battlefield
        _combatManager.StartCombat(activePlayer);

        // Act & Assert
        _combatManager.Invoking(c => c.DeclareAttackers(activePlayer, new[]
        {
            new AttackerDeclaration(creature, targetPlayer)
        })).Should().Throw<InvalidPlayerActionException>();
    }

    [Fact]
    public void DeclareBlockers_ValidBlockers_AddsBlockers()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = defendingPlayer };
        attackerCreature.Zone = ZoneType.Battlefield;
        blockerCreature.Zone = ZoneType.Battlefield;
        attackerCreature.HasSummoningSickness = false;
        _combatManager.StartCombat(activePlayer);
        _combatManager.DeclareAttackers(activePlayer, new[]
        {
            new AttackerDeclaration(attackerCreature, defendingPlayer)
        });
        var attacker = _combatManager.CurrentCombat!.Attackers[0];

        // Act
        _combatManager.DeclareBlockers(defendingPlayer, new[]
        {
            new BlockerDeclaration(blockerCreature, attacker)
        });

        // Assert
        attacker.Blockers.Should().HaveCount(1);
        _eventBusMock.Verify(x => x.Publish(It.IsAny<BlockersDeclaredEvent>()), Times.Once);
    }

    [Fact]
    public void DeclareBlockers_NoCombat_ThrowsException()
    {
        // Arrange
        var defendingPlayer = new Player("Bob", 20);
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = defendingPlayer };
        var attacker = new Attacker(new Creature("Grizzly Bears", "1G", 2, 2), defendingPlayer);

        // Act & Assert
        _combatManager.Invoking(c => c.DeclareBlockers(defendingPlayer, new[]
        {
            new BlockerDeclaration(blockerCreature, attacker)
        })).Should().Throw<InvalidGameStateException>();
    }

    [Fact]
    public void AssignCombatDamage_UnblockedAttacker_DealsDamageToPlayer()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        creature.Zone = ZoneType.Battlefield;
        creature.HasSummoningSickness = false;
        _combatManager.StartCombat(activePlayer);
        _combatManager.DeclareAttackers(activePlayer, new[]
        {
            new AttackerDeclaration(creature, targetPlayer)
        });
        _combatManager.DeclareBlockers(targetPlayer, Array.Empty<BlockerDeclaration>());

        // Act
        _combatManager.AssignCombatDamage();

        // Assert
        targetPlayer.LifeTotal.Should().Be(18); // 20 - 2 = 18
        _eventBusMock.Verify(x => x.Publish(It.Is<CombatDamageDealtEvent>(e => 
            e.Source == creature && e.TargetPlayer == targetPlayer && e.Amount == 2)), Times.Once);
    }

    [Fact]
    public void AssignCombatDamage_BlockedAttacker_DealsDamageToBlocker()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = defendingPlayer };
        attackerCreature.Zone = ZoneType.Battlefield;
        blockerCreature.Zone = ZoneType.Battlefield;
        attackerCreature.HasSummoningSickness = false;
        _combatManager.StartCombat(activePlayer);
        _combatManager.DeclareAttackers(activePlayer, new[]
        {
            new AttackerDeclaration(attackerCreature, defendingPlayer)
        });
        var attacker = _combatManager.CurrentCombat!.Attackers[0];
        _combatManager.DeclareBlockers(defendingPlayer, new[]
        {
            new BlockerDeclaration(blockerCreature, attacker)
        });

        // Act
        _combatManager.AssignCombatDamage();

        // Assert
        blockerCreature.Damage.Should().Be(2); // Takes 2 damage from attacker
        attackerCreature.Damage.Should().Be(1); // Takes 1 damage from blocker
        _eventBusMock.Verify(x => x.Publish(It.Is<CombatDamageDealtEvent>(e => 
            e.Source == attackerCreature && e.Target == blockerCreature)), Times.Once);
        _eventBusMock.Verify(x => x.Publish(It.Is<CombatDamageDealtEvent>(e => 
            e.Source == blockerCreature && e.Target == attackerCreature)), Times.Once);
    }

    [Fact]
    public void AssignCombatDamage_NoCombat_ThrowsException()
    {
        // Act & Assert
        _combatManager.Invoking(c => c.AssignCombatDamage())
            .Should().Throw<InvalidGameStateException>();
    }

    [Fact]
    public void EndCombat_EndsCombat()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        creature.Zone = ZoneType.Battlefield;
        creature.HasSummoningSickness = false;
        _combatManager.StartCombat(activePlayer);
        _combatManager.DeclareAttackers(activePlayer, new[]
        {
            new AttackerDeclaration(creature, targetPlayer)
        });

        // Act
        _combatManager.EndCombat();

        // Assert
        _combatManager.IsInCombat.Should().BeFalse();
        _combatManager.CurrentCombat.Should().BeNull();
        _eventBusMock.Verify(x => x.Publish(It.IsAny<CombatEndedEvent>()), Times.Once);
    }

    [Fact]
    public void EndCombat_NoCombat_DoesNothing()
    {
        // Act
        _combatManager.EndCombat();

        // Assert
        _combatManager.IsInCombat.Should().BeFalse();
        _combatManager.CurrentCombat.Should().BeNull();
    }

    [Fact]
    public void AssignCombatDamage_LethalDamage_KillsCreature()
    {
        // Arrange
        var activePlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = activePlayer };
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = defendingPlayer };
        attackerCreature.Zone = ZoneType.Battlefield;
        blockerCreature.Zone = ZoneType.Battlefield;
        attackerCreature.HasSummoningSickness = false;
        _combatManager.StartCombat(activePlayer);
        _combatManager.DeclareAttackers(activePlayer, new[]
        {
            new AttackerDeclaration(attackerCreature, defendingPlayer)
        });
        var attacker = _combatManager.CurrentCombat!.Attackers[0];
        _combatManager.DeclareBlockers(defendingPlayer, new[]
        {
            new BlockerDeclaration(blockerCreature, attacker)
        });

        // Act
        _combatManager.AssignCombatDamage();

        // Assert
        blockerCreature.IsDead().Should().BeTrue(); // 1 toughness, 2 damage
        _eventBusMock.Verify(x => x.Publish(It.IsAny<StateBasedActionExecutedEvent>()), Times.AtLeastOnce);
    }
}
