using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="DelightedHalflingFactory"/>.</summary>
public class DelightedHalflingTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Power / toughness
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_OneTwo()
    {
        var hh = DelightedHalflingFactory.Create(_alice);

        hh.Power.Should().Be(1);
        hh.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_HasFiveManaAbilities_OnePerColor()
    {
        var hh = DelightedHalflingFactory.Create(_alice);
        var mas = hh.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");
    }

    // -----------------------------------------------------------------------
    // Subtypes
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_HasHalflingAndCitizenSubtypes()
    {
        var hh = DelightedHalflingFactory.Create(_alice);

        hh.HasSubtype(CardSubtype.Halfling).Should().BeTrue("Delighted Halfling is a Halfling");
        hh.HasSubtype(CardSubtype.Citizen).Should().BeTrue("Delighted Halfling is a Citizen");
    }

    // -----------------------------------------------------------------------
    // Supertype
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_IsLegendary()
    {
        var hh = DelightedHalflingFactory.Create(_alice);

        hh.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Delighted Halfling is a Legendary Creature");
    }

    // -----------------------------------------------------------------------
    // Owner / controller
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_OwnerAndControllerAreSet()
    {
        var hh = DelightedHalflingFactory.Create(_alice);

        hh.Owner.Should().BeSameAs(_alice);
        hh.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana cost
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_ManaCostIsGreen()
    {
        var hh = DelightedHalflingFactory.Create(_alice);

        hh.ManaCost.Should().Be("{G}");
    }
}
