using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Unit tests for ActionValidator service.
/// Tests action validation and validation results.
/// </summary>
public class ActionValidatorTests
{
    private readonly ActionValidator _validator;

    public ActionValidatorTests()
    {
        _validator = new ActionValidator();
    }

    [Fact]
    public void ValidateAction_NullAction_ReturnsInvalid()
    {
        // Act
        var result = _validator.ValidateAction(null!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null");
    }

    [Fact]
    public void ValidateAction_CastSpellAction_ReturnsValid()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var action = new CastSpellAction(card, player);

        // Act
        var result = _validator.ValidateAction(action);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateAction_ActivateAbilityAction_ReturnsValid()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var artifact = new Artifact("Staff", "") { Owner = player, Controller = player };
        artifact.SetZone(ZoneType.Battlefield);
        var ability = new ActivatedAbility(artifact, player, null, new List<ICost>(), new List<IEffect>());
        var action = new ActivateAbilityAction(ability, player);

        // Act
        var result = _validator.ValidateAction(action);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateAction_AttackAction_ReturnsValid()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = player, Controller = player };
        var action = new AttackAction(creature, player);

        // Act
        var result = _validator.ValidateAction(action);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateAction_BlockAction_ReturnsValid()
    {
        // Arrange
        var attackingPlayer = new Player("Alice", 20);
        var defendingPlayer = new Player("Bob", 20);
        var attackerCreature = new Creature("Grizzly Bears", "1G", 2, 2) { Controller = attackingPlayer };
        var blockerCreature = new Creature("Lightning Bolt", "R", 1, 1) { Controller = defendingPlayer };
        var attacker = new Attacker(attackerCreature, defendingPlayer);
        var action = new BlockAction(blockerCreature, attacker, defendingPlayer);

        // Act
        var result = _validator.ValidateAction(action);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidationResult_Valid_CreatesValidResult()
    {
        // Act
        var result = ValidationResult.Valid();

        // Assert
        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.Violation.Should().BeNull();
    }

    [Fact]
    public void ValidationResult_Invalid_CreatesInvalidResult()
    {
        // Act
        var result = ValidationResult.Invalid("Test error");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Test error");
    }

    [Fact]
    public void ValidationResult_InvalidWithViolation_CreatesInvalidResult()
    {
        // Arrange
        var violation = new RuleViolation("704.5", "Test violation");

        // Act
        var result = ValidationResult.Invalid("Test error", violation);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violation.Should().Be(violation);
    }
}
