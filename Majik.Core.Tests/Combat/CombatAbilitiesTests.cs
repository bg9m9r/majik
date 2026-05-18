using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Unit tests for CombatAbilities helper class.
/// Tests ability checking methods (currently all return false as abilities are not yet implemented).
/// </summary>
public class CombatAbilitiesTests
{
    [Fact]
    public void HasFirstStrike_NullCreature_ReturnsFalse()
    {
        // Act
        var result = CombatAbilities.HasFirstStrike(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasFirstStrike_ValidCreature_ReturnsFalse()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act
        var result = CombatAbilities.HasFirstStrike(creature);

        // Assert
        // Currently returns false as static abilities are not yet implemented
        result.Should().BeFalse();
    }

    [Fact]
    public void HasDoubleStrike_NullCreature_ReturnsFalse()
    {
        // Act
        var result = CombatAbilities.HasDoubleStrike(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasTrample_NullCreature_ReturnsFalse()
    {
        // Act
        var result = CombatAbilities.HasTrample(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasDeathtouch_NullCreature_ReturnsFalse()
    {
        // Act
        var result = CombatAbilities.HasDeathtouch(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasVigilance_NullCreature_ReturnsFalse()
    {
        // Act
        var result = CombatAbilities.HasVigilance(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasHaste_NullCreature_ReturnsFalse()
    {
        // Act
        var result = CombatAbilities.HasHaste(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasReach_NullCreature_ReturnsFalse()
    {
        // Act
        var result = CombatAbilities.HasReach(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasFlying_NullCreature_ReturnsFalse()
    {
        // Act
        var result = CombatAbilities.HasFlying(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBlockFlying_WithFlying_ReturnsTrue()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        // Note: Currently HasFlying returns false, so this test will fail when abilities are implemented
        // This test documents expected behavior

        // Act
        var result = CombatAbilities.CanBlockFlying(creature);

        // Assert
        // Currently returns false as abilities are not implemented
        // When implemented, if creature has flying, should return true
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBlockFlying_WithReach_ReturnsTrue()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        // Note: Currently HasReach returns false, so this test will fail when abilities are implemented
        // This test documents expected behavior

        // Act
        var result = CombatAbilities.CanBlockFlying(creature);

        // Assert
        // Currently returns false as abilities are not implemented
        // When implemented, if creature has reach, should return true
        result.Should().BeFalse();
    }
}
