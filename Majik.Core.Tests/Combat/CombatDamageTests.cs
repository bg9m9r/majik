using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Unit tests for CombatDamage value object.
/// Tests damage creation, validation, and equality.
/// </summary>
public class CombatDamageTests
{
    [Fact]
    public void ToCreature_ValidInput_CreatesDamage()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Creature("Lightning Bolt", "R", 1, 1);

        // Act
        var damage = CombatDamage.ToCreature(source, target, 2);

        // Assert
        damage.Source.Should().Be(source);
        damage.Target.Should().Be(target);
        damage.Amount.Should().Be(2);
        damage.IsCombatDamage.Should().BeTrue();
        damage.IsLethal.Should().BeTrue(); // 2 >= 1 toughness
    }

    [Fact]
    public void ToCreature_WithDeathtouch_CreatesLethalDamage()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Creature("Lightning Bolt", "R", 1, 5);

        // Act
        var damage = CombatDamage.ToCreature(source, target, 1, hasDeathtouch: true);

        // Assert
        damage.Amount.Should().Be(1);
        damage.IsLethal.Should().BeTrue(); // Deathtouch makes 1 damage lethal
    }

    [Fact]
    public void ToCreature_NullSource_ThrowsException()
    {
        // Arrange
        var target = new Creature("Lightning Bolt", "R", 1, 1);

        // Act & Assert
        new Action(() => CombatDamage.ToCreature(null!, target, 1))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToCreature_NullTarget_ThrowsException()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);

        // Act & Assert
        new Action(() => CombatDamage.ToCreature(source, null!, 1))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToCreature_NegativeAmount_ThrowsException()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Creature("Lightning Bolt", "R", 1, 1);

        // Act & Assert
        new Action(() => CombatDamage.ToCreature(source, target, -1))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToPlayer_ValidInput_CreatesDamage()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Player("Bob", 20);

        // Act
        var damage = CombatDamage.ToPlayer(source, target, 3);

        // Assert
        damage.Source.Should().Be(source);
        damage.Target.Should().BeNull(); // Player is not an ICard
        damage.Amount.Should().Be(3);
        damage.IsCombatDamage.Should().BeTrue();
        damage.IsLethal.Should().BeFalse(); // Not applicable for players
    }

    [Fact]
    public void ToPlaneswalker_ValidInput_CreatesDamage()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Planeswalker("Jace", "2UU", 3);

        // Act
        var damage = CombatDamage.ToPlaneswalker(source, target, 3);

        // Assert
        damage.Source.Should().Be(source);
        damage.Target.Should().Be(target);
        damage.Amount.Should().Be(3);
        damage.IsLethal.Should().BeTrue(); // 3 >= 3 loyalty
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Creature("Lightning Bolt", "R", 1, 1);
        var damage1 = CombatDamage.ToCreature(source, target, 2);
        var damage2 = CombatDamage.ToCreature(source, target, 2);

        // Act & Assert
        damage1.Equals(damage2).Should().BeTrue();
        (damage1 == damage2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentAmounts_ReturnsFalse()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Creature("Lightning Bolt", "R", 1, 1);
        var damage1 = CombatDamage.ToCreature(source, target, 2);
        var damage2 = CombatDamage.ToCreature(source, target, 1);

        // Act & Assert
        damage1.Equals(damage2).Should().BeFalse();
        (damage1 != damage2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHashCode()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Creature("Lightning Bolt", "R", 1, 1);
        var damage1 = CombatDamage.ToCreature(source, target, 2);
        var damage2 = CombatDamage.ToCreature(source, target, 2);

        // Act & Assert
        damage1.GetHashCode().Should().Be(damage2.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsReadableFormat()
    {
        // Arrange
        var source = new Creature("Grizzly Bears", "1G", 2, 2);
        var target = new Creature("Lightning Bolt", "R", 1, 1);
        var damage = CombatDamage.ToCreature(source, target, 2);

        // Act
        var result = damage.ToString();

        // Assert
        result.Should().Contain("Grizzly Bears");
        result.Should().Contain("Lightning Bolt");
        result.Should().Contain("2");
    }
}
