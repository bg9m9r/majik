using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Unit tests for Creature entity.
/// Tests power, toughness, damage, and death.
/// </summary>
public class CreatureTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesCreature()
    {
        // Act
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Assert
        creature.Name.Should().Be("Grizzly Bears");
        creature.BasePower.Should().Be(2);
        creature.BaseToughness.Should().Be(2);
        creature.Power.Should().Be(2);
        creature.Toughness.Should().Be(2);
        creature.Damage.Should().Be(0);
        creature.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void BasePower_NegativeValue_ThrowsException()
    {
        // Arrange
        var creature = new Creature("Test", "", 2, 2);

        // Act & Assert
        creature.Invoking(c => c.BasePower = -1)
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BaseToughness_NegativeValue_ThrowsException()
    {
        // Arrange
        var creature = new Creature("Test", "", 2, 2);

        // Act & Assert
        creature.Invoking(c => c.BaseToughness = -1)
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TakeDamage_ValidAmount_IncreasesDamage()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act
        creature.TakeDamage(1);

        // Assert
        creature.Damage.Should().Be(1);
        creature.IsDead().Should().BeFalse();
    }

    [Fact]
    public void TakeDamage_NegativeAmount_ThrowsException()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act & Assert
        creature.Invoking(c => c.TakeDamage(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TakeDamage_DamageEqualsToughness_IsDead()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act
        creature.TakeDamage(2);

        // Assert
        creature.Damage.Should().Be(2);
        creature.IsDead().Should().BeTrue();
    }

    [Fact]
    public void TakeDamage_DamageExceedsToughness_IsDead()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act
        creature.TakeDamage(3);

        // Assert
        creature.Damage.Should().Be(3);
        creature.IsDead().Should().BeTrue();
    }

    [Fact]
    public void RemoveDamage_ValidAmount_DecreasesDamage()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        creature.TakeDamage(2);

        // Act
        creature.RemoveDamage(1);

        // Assert
        creature.Damage.Should().Be(1);
    }

    [Fact]
    public void RemoveDamage_MoreThanDamage_ResultsInZero()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        creature.TakeDamage(1);

        // Act
        creature.RemoveDamage(5);

        // Assert
        creature.Damage.Should().Be(0);
    }

    [Fact]
    public void RemoveDamage_NegativeAmount_ThrowsException()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act & Assert
        creature.Invoking(c => c.RemoveDamage(-1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ClearDamage_RemovesAllDamage()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        creature.TakeDamage(2);

        // Act
        creature.ClearDamage();

        // Assert
        creature.Damage.Should().Be(0);
    }

    [Fact]
    public void GetPower_ReturnsBasePower()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act & Assert
        creature.GetPower().Should().Be(2);
        creature.Power.Should().Be(2);
    }

    [Fact]
    public void GetToughness_ReturnsBaseToughness()
    {
        // Arrange
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act & Assert
        creature.GetToughness().Should().Be(2);
        creature.Toughness.Should().Be(2);
    }
}
