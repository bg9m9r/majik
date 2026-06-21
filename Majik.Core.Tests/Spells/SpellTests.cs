using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.ValueObjects;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Spells;

/// <summary>
/// Unit tests for Spell class.
/// Tests spell creation, resolution, and zone determination.
/// </summary>
public class SpellTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesSpell()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };

        // Act
        var spell = new Spell(card, player);

        // Assert
        spell.Card.Should().Be(card);
        spell.Controller.Should().Be(player);
        spell.Targets.Should().BeEmpty();
        spell.Costs.Should().BeEmpty();
        spell.Effects.Should().BeEmpty();
        spell.IsResolving.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullCard_ThrowsException()
    {
        // Arrange
        var player = new Player("Alice", 20);

        // Act & Assert
        new Action(() => new Spell(null!, player))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullController_ThrowsException()
    {
        // Arrange
        var card = new Instant("Lightning Bolt", "R");

        // Act & Assert
        new Action(() => new Spell(card, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithTargets_StoresTargets()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var targetPlayer = new Player("Bob", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var targets = new List<ITarget> { Target.Player(targetPlayer) };

        // Act
        var spell = new Spell(card, player, targets);

        // Assert
        spell.Targets.Should().HaveCount(1);
        spell.Targets[0].Should().Be(targets[0]);
    }

    [Fact]
    public void Constructor_WithCosts_StoresCosts()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var costs = new List<ICost> { new ManaCostCost("R") };

        // Act
        var spell = new Spell(card, player, null, costs);

        // Assert
        spell.Costs.Should().HaveCount(1);
    }

    [Fact]
    public void Constructor_WithEffects_StoresEffects()
    {
        // Arrange
        var player = new Player("Alice", 20);
        var card = new Instant("Lightning Bolt", "R") { Owner = player };
        var effects = new List<IEffect> { new Effect("Deal 3 damage", () => { }) };

        // Act
        var spell = new Spell(card, player, null, null, effects);

        // Assert
        spell.Effects.Should().HaveCount(1);
    }

}
