using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Spells;
using Xunit;

namespace Majik.Core.Tests.Mana;

/// <summary>
/// Integration tests pinning the spend-restriction stamp on Cavern of
/// Souls + Eldrazi Temple. The payment-gate wiring is deferred (see
/// <see cref="SpendRestrictionTests"/> xmldoc) — these tests verify the
/// factory side only: the right <see cref="ManaAbility"/> instances
/// carry the right <see cref="SpendRestriction"/>.
/// </summary>
public class SpendRestrictionFactoryWiringTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Cavern of Souls
    // -----------------------------------------------------------------------

    [Fact]
    public void CavernOfSouls_AnyColorAbilities_StampCreatureSpellRestriction()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        // 1 colorless + 5 any-color = 6 mana abilities.
        manaAbilities.Should().HaveCount(6);

        // The first (printed {T}: Add {C}) is unrestricted.
        var colorlessAbility = manaAbilities.First(a => a.ManaGenerated.Generic == 1);
        colorlessAbility.SpendRestriction.Should().BeNull(
            "Cavern's printed {T}: Add {C} has no spend-restriction");

        // The five any-color abilities all carry the creature-spell restriction.
        var restricted = manaAbilities.Where(a => a.SpendRestriction != null).ToList();
        restricted.Should().HaveCount(5, "Cavern wires 5 any-color mana abilities, each restricted");

        foreach (var ab in restricted)
        {
            ab.SpendRestriction!.Description.Should().Contain("creature spell");
        }
    }

    [Fact]
    public void CavernOfSouls_NoChosenType_RestrictionPermitsAnyCreature()
    {
        var land = CavernOfSoulsFactory.Create(_alice);
        var anyColorAbility = land.Abilities.OfType<ManaAbility>()
            .First(a => a.SpendRestriction != null);

        var creature = MakeCreature("Tarmogoyf", subtypes: new[] { CardSubtype.Lhurgoyf });
        var instant = new Instant("Lightning Bolt", "R");

        anyColorAbility.SpendRestriction!.SatisfiedBy(MakeSpell(creature))
            .Should().BeTrue("any creature spell qualifies when no type was chosen");
        anyColorAbility.SpendRestriction.SatisfiedBy(MakeSpell(instant))
            .Should().BeFalse("Lightning Bolt is not a creature spell");
    }

    [Fact]
    public void CavernOfSouls_WithChosenType_RestrictionRefinesToSubtype()
    {
        var land = CavernOfSoulsFactory.Create(_alice, _ => CardSubtype.Merfolk);
        var anyColorAbility = land.Abilities.OfType<ManaAbility>()
            .First(a => a.SpendRestriction != null);

        var merfolk = MakeCreature("Lord of Atlantis", subtypes: new[] { CardSubtype.Merfolk });
        var goblin = MakeCreature("Goblin Guide", subtypes: new[] { CardSubtype.Goblin });

        anyColorAbility.SpendRestriction!.SatisfiedBy(MakeSpell(merfolk))
            .Should().BeTrue("chosen Merfolk + creature subtype matches");
        anyColorAbility.SpendRestriction.SatisfiedBy(MakeSpell(goblin))
            .Should().BeFalse("Goblin is a creature but wrong chosen type");
    }

    // -----------------------------------------------------------------------
    // Eldrazi Temple
    // -----------------------------------------------------------------------

    [Fact]
    public void EldraziTemple_CCAbility_StampsEldraziRestriction()
    {
        var land = EldraziTempleFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2);

        var singleC = manaAbilities.Single(a => a.ManaGenerated.Generic == 1);
        var doubleC = manaAbilities.Single(a => a.ManaGenerated.Generic == 2);

        singleC.SpendRestriction.Should().BeNull(
            "Eldrazi Temple's {T}: Add {C} is unrestricted per the printed oracle");
        doubleC.SpendRestriction.Should().NotBeNull(
            "{T}: Add {C}{C} carries the 'spend only on Eldrazi' rider");
        doubleC.SpendRestriction!.Description.Should().Contain("Eldrazi");
    }

    [Fact]
    public void EldraziTemple_CCRestriction_PermitsEldraziSpell()
    {
        var land = EldraziTempleFactory.Create(_alice);
        var doubleC = land.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Generic == 2);

        var endlessOne = MakeCreature("Endless One", subtypes: new[] { CardSubtype.Eldrazi });

        doubleC.SpendRestriction!.SatisfiedBy(MakeSpell(endlessOne))
            .Should().BeTrue("Endless One is an Eldrazi — qualifies");
    }

    [Fact]
    public void EldraziTemple_CCRestriction_RejectsKarnLiberated()
    {
        var land = EldraziTempleFactory.Create(_alice);
        var doubleC = land.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Generic == 2);

        // Karn Liberated — colorless planeswalker, not an Eldrazi.
        var karn = new Card(
            "Karn Liberated",
            manaCost: "7",
            cardTypes: new[] { CardType.Planeswalker });

        doubleC.SpendRestriction!.SatisfiedBy(MakeSpell(karn))
            .Should().BeFalse("Karn isn't Eldrazi — restriction rejects");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Majik.Core.Spells.Spell MakeSpell(ICard card) => new(card, _alice);

    private static Creature MakeCreature(string name, IEnumerable<CardSubtype>? subtypes = null)
        => new(name, manaCost: "1G", power: 2, toughness: 2, supertypes: null, subtypes: subtypes);
}
