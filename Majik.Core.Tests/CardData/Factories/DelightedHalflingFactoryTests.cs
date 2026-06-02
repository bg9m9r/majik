using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DelightedHalflingFactory"/>.
///
/// Delighted Halfling (The Lord of the Rings: Tales of Middle-earth, {G}).
/// Creature — Halfling Citizen 1/2. Oracle text:
///   "{T}: Add {C}.
///    {T}: Add one mana of any color. Spend this mana only to cast a
///    legendary spell, and that spell can't be countered."
///
/// The {T}: Add {C} ability comes from the JSON definition; the five
/// "add one mana of any color" ManaAbilities (carrying the legendary-only
/// SpendRestriction) are wired in the factory — same shape as Cavern of
/// Souls / Ornithopter of Paradise.
/// </summary>
public class DelightedHalflingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DelightedHalfling_Identity()
    {
        var c = DelightedHalflingFactory.Create(_alice);

        c.Name.Should().Be("Delighted Halfling");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Halfling).Should().BeTrue();
        c.HasSubtype(CardSubtype.Citizen).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DelightedHalfling_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Delighted Halfling", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Delighted Halfling");
        ((Creature)c).HasSubtype(CardSubtype.Halfling).Should().BeTrue();
    }

    [Fact]
    public void DelightedHalfling_HasSixManaAbilities_ColorlessPlusFiveColors()
    {
        // "{T}: Add {C}" (from JSON) + five "Add one mana of any color"
        // ManaAbility instances (one per WUBRG) from the factory.
        var c = DelightedHalflingFactory.Create(_alice);
        var mas = c.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(6, "{T}: Add {C} plus one ManaAbility per WUBRG colour");
    }

    [Fact]
    public void DelightedHalfling_AnyColorAbilities_CoverEveryColor()
    {
        var c = DelightedHalflingFactory.Create(_alice);

        // ManaCost.ToString() returns bare colour letters — no braces.
        // The five RESTRICTED any-colour abilities are the ones carrying a
        // SpendRestriction; the {C} ability is unrestricted.
        var restrictedColors = c.Abilities.OfType<ManaAbility>()
            .Where(a => a.SpendRestriction != null)
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        restrictedColors.Should().BeEquivalentTo(new[] { "B", "G", "R", "U", "W" },
            "the 'add one mana of any color' ability is five colour ManaAbilities.");
    }

    [Fact]
    public void DelightedHalfling_ColorlessAbility_IsUnrestricted()
    {
        // CR 205.4a / printed oracle — only the second mana ability carries
        // the "spend this mana only to cast a legendary spell" rider.
        var c = DelightedHalflingFactory.Create(_alice);

        var colorlessAbility = c.Abilities.OfType<ManaAbility>()
            .Single(a => a.SpendRestriction == null);

        colorlessAbility.ManaGenerated!.Generic.Should().Be(1,
            "{T}: Add {C} folds into the generic bucket.");
    }

    [Fact]
    public void DelightedHalfling_AnyColorAbilities_CarryLegendaryOnlyRestriction()
    {
        var c = DelightedHalflingFactory.Create(_alice);

        var restricted = c.Abilities.OfType<ManaAbility>()
            .Where(a => a.SpendRestriction != null)
            .ToList();

        restricted.Should().HaveCount(5,
            "all five any-colour abilities carry the spend-restriction.");
        restricted.Should().OnlyContain(
            a => a.SpendRestriction!.Description == "legendary spell",
            "the rider is 'spend this mana only to cast a legendary spell'.");
    }

    [Fact]
    public void DelightedHalfling_LegendaryRestriction_AcceptsLegendaryRejectsNonLegendary()
    {
        // CR 106.4 — the restriction's predicate is evaluated against the
        // spell being paid for. A legendary spell satisfies it; a
        // non-legendary spell does not.
        var c = DelightedHalflingFactory.Create(_alice);
        var restriction = c.Abilities.OfType<ManaAbility>()
            .First(a => a.SpendRestriction != null).SpendRestriction!;

        var legendary = new Creature(
            "Some Legend", "{1}{G}", 2, 2,
            supertypes: new[] { CardSupertype.Legendary });
        var vanilla = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        restriction.SatisfiedBy(new Majik.Core.Spells.Spell(legendary, _alice)).Should().BeTrue(
            "a legendary spell may be paid with this mana.");
        restriction.SatisfiedBy(new Majik.Core.Spells.Spell(vanilla, _alice)).Should().BeFalse(
            "a non-legendary spell may not be paid with this mana.");
    }

    [Fact]
    public void DelightedHalfling_ColorlessAbility_ProducesColorlessAndTaps()
    {
        var c = DelightedHalflingFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        // CR 302.6 — clear summoning sickness so we exercise mana production
        // rather than the {T} sickness gate.
        c.ClearSummoningSickness();

        var colorlessAbility = c.Abilities.OfType<ManaAbility>()
            .Single(a => a.SpendRestriction == null);

        colorlessAbility.CanActivate().Should().BeTrue("creature is untapped.");
        var mana = colorlessAbility.Activate();
        mana.Generic.Should().Be(1);
        c.IsTapped.Should().BeTrue("activating the {T} mana ability taps the Halfling.");
    }
}
