using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.Stack;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Unit tests for RulesEngine service.
/// Tests rules validation for various game actions.
/// </summary>
public class RulesEngineTests
{
    private readonly RulesEngine _rulesEngine;

    public RulesEngineTests()
    {
        _rulesEngine = new RulesEngine();
    }

    [Fact]
    public void CanCastSpell_InstantInHand_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var instant = new Instant("Lightning Bolt", "R") { Owner = player };
        instant.SetZone(ZoneType.Hand);

        // Act
        var result = _rulesEngine.CanCastSpell(instant, player, isMainPhase: false, isStackEmpty: true);

        // Assert
        result.Should().BeTrue(); // Instants can be cast anytime
    }

    [Fact]
    public void CanCastSpell_SorceryInHandMainPhase_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var sorcery = new Sorcery("Fireball", "2RR") { Owner = player };
        sorcery.SetZone(ZoneType.Hand);

        // Act
        var result = _rulesEngine.CanCastSpell(sorcery, player, isMainPhase: true, isStackEmpty: true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanCastSpell_SorceryNotMainPhase_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var sorcery = new Sorcery("Fireball", "2RR") { Owner = player };
        sorcery.SetZone(ZoneType.Hand);

        // Act
        var result = _rulesEngine.CanCastSpell(sorcery, player, isMainPhase: false, isStackEmpty: true);

        // Assert
        result.Should().BeFalse(); // Sorceries can only be cast in main phase
    }

    [Fact]
    public void CanCastSpell_SorceryStackNotEmpty_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var sorcery = new Sorcery("Fireball", "2RR") { Owner = player };
        sorcery.SetZone(ZoneType.Hand);

        // Act
        var result = _rulesEngine.CanCastSpell(sorcery, player, isMainPhase: true, isStackEmpty: false);

        // Assert
        result.Should().BeFalse(); // Sorceries can only be cast when stack is empty
    }

    [Fact]
    public void CanCastSpell_CardNotInHand_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var instant = new Instant("Lightning Bolt", "R") { Owner = player };
        instant.SetZone(ZoneType.Battlefield); // Not in hand

        // Act
        var result = _rulesEngine.CanCastSpell(instant, player, isMainPhase: false, isStackEmpty: true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanPayMana_SufficientMana_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("RR"));
        var cost = ManaCost.Parse("R");

        // Act
        var result = _rulesEngine.CanPayMana(player, cost);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanPayMana_InsufficientMana_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.AddManaToPool(ManaCost.Parse("R"));
        var cost = ManaCost.Parse("RR");

        // Act
        var result = _rulesEngine.CanPayMana(player, cost);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanMoveCard_ValidTransition_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        card.SetZone(ZoneType.Hand);

        // Act
        var result = _rulesEngine.CanMoveCard(card, ZoneType.Hand, ZoneType.Battlefield);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanMoveCard_WrongSourceZone_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        card.SetZone(ZoneType.Hand);

        // Act
        var result = _rulesEngine.CanMoveCard(card, ZoneType.Battlefield, ZoneType.Graveyard);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanCastInPhase_Instant_ReturnsTrue()
    {
        // Arrange
        var instant = new Instant("Lightning Bolt", "R");

        // Act
        var result = _rulesEngine.CanCastInPhase(instant, PhaseStateType.CombatDamage, isStackEmpty: false);

        // Assert
        result.Should().BeTrue(); // Instants can be cast anytime
    }

    [Fact]
    public void CanCastInPhase_SorceryMainPhase_ReturnsTrue()
    {
        // Arrange
        var sorcery = new Sorcery("Fireball", "2RR");

        // Act
        var result = _rulesEngine.CanCastInPhase(sorcery, PhaseStateType.PreCombatMain, isStackEmpty: true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanCastInPhase_SorceryNotMainPhase_ReturnsFalse()
    {
        // Arrange
        var sorcery = new Sorcery("Fireball", "2RR");

        // Act
        var result = _rulesEngine.CanCastInPhase(sorcery, PhaseStateType.CombatDamage, isStackEmpty: true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanTakeAction_PlayerNotLost_ReturnsTrue()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act
        var result = _rulesEngine.CanTakeAction(player, isStackEmpty: true, allPlayersPassed: true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanTakeAction_PlayerHasLost_ReturnsFalse()
    {
        // Arrange
        var player = new Player("Alice", 20);
        player.HasLost = true;

        // Act
        var result = _rulesEngine.CanTakeAction(player, isStackEmpty: true, allPlayersPassed: true);

        // Assert
        result.Should().BeFalse();
    }
}
