using Majik.Core.CardData;
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
        var hh = (Creature)NamedCardFactory.Create("Delighted Halfling", _alice);

        hh.Power.Should().Be(1);
        hh.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_HasSixManaAbilities_ColorlessPlusOnePerColor()
    {
        // Oracle: "{T}: Add {C}." (one ManaAbility, unrestricted) PLUS
        // "{T}: Add one mana of any color." (five ManaAbilities, one per
        // WUBRG, each carrying the legendary-only SpendRestriction).
        var hh = (Creature)NamedCardFactory.Create("Delighted Halfling", _alice);
        var mas = hh.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(6, "{T}: Add {C} plus one ManaAbility per WUBRG colour");
    }

    // -----------------------------------------------------------------------
    // Subtypes
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_HasHalflingAndCitizenSubtypes()
    {
        var hh = (Creature)NamedCardFactory.Create("Delighted Halfling", _alice);

        hh.HasSubtype(CardSubtype.Halfling).Should().BeTrue("Delighted Halfling is a Halfling");
        hh.HasSubtype(CardSubtype.Citizen).Should().BeTrue("Delighted Halfling is a Citizen");
    }

    // -----------------------------------------------------------------------
    // Supertype — Delighted Halfling is a plain (NON-legendary) creature.
    // Its mana lets you cast OTHER legendary spells; the card itself carries
    // no Legendary supertype (Scryfall type_line: "Creature — Halfling
    // Citizen"). The prior assertion here was factually wrong.
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_IsNotLegendary()
    {
        var hh = (Creature)NamedCardFactory.Create("Delighted Halfling", _alice);

        hh.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Delighted Halfling is a plain Creature — Halfling Citizen; it is not itself legendary");
    }

    // -----------------------------------------------------------------------
    // Owner / controller
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_OwnerAndControllerAreSet()
    {
        var hh = (Creature)NamedCardFactory.Create("Delighted Halfling", _alice);

        hh.Owner.Should().BeSameAs(_alice);
        hh.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana cost
    // -----------------------------------------------------------------------

    [Fact]
    public void DelightedHalfling_ManaCostIsGreen()
    {
        var hh = (Creature)NamedCardFactory.Create("Delighted Halfling", _alice);

        hh.ManaCost.Should().Be("{G}");
    }
}
