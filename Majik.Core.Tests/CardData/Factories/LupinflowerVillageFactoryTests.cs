using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LupinflowerVillageFactory"/> — Lupinflower Village
/// (Bloomburrow).
///
/// Land. Oracle text:
///   "{T}: Add {C}."
///   "{T}: Add {W}. Spend this mana only to cast a creature spell."
///   "{1}{W}, {T}, Sacrifice this land: Look at the top six cards of your
///    library. You may reveal a Bat, Bird, Mouse, or Rabbit card from among
///    them and put it into your hand. Put the rest on the bottom of your
///    library in a random order."
///
/// The contract test (<c>CardFactoryContractTests</c>) already asserts dispatch
/// + well-formedness; these tests cover the card's UNIQUE behaviour: the two
/// mana abilities (one restricted), the {1}{W},{T},Sacrifice dig ability, and
/// the look-at-top-six reveal-by-creature-subtype dig resolution.
/// </summary>
[Trait("Color", "W")]
public class LupinflowerVillageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeCreature(
        string name, params CardSubtype[] subtypes) =>
        new(name, "{1}{W}", 1, 1, supertypes: null, subtypes: subtypes);

    private void SeedLibrary(params ICard[] cards)
    {
        foreach (var c in cards)
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(Core.Zones.ZoneType.Library);
        }
    }

    // =========================================================================
    // Identity
    // =========================================================================

    [Fact]
    public void Identity_Land_NonBasic_NoSubtype()
    {
        var land = LupinflowerVillageFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Lupinflower Village");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Lupinflower Village is non-basic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // =========================================================================
    // Mana abilities
    // =========================================================================

    [Fact]
    public void HasUnrestrictedColorlessManaAbility_AddingC()
    {
        var land = LupinflowerVillageFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().Contain(
            a => a.ManaGenerated.Equals(ManaCost.Parse("C")) && a.SpendRestriction == null,
            "{T}: Add {C} is unrestricted colourless mana");
    }

    [Fact]
    public void HasCreatureSpellRestrictedManaAbility_AddingW()
    {
        var land = LupinflowerVillageFactory.Create(_alice);

        var whiteAbility = land.Abilities.OfType<ManaAbility>()
            .SingleOrDefault(a => a.ManaGenerated.Equals(ManaCost.Parse("W")));

        whiteAbility.Should().NotBeNull("{T}: Add {W} is wired");
        whiteAbility!.SpendRestriction.Should().NotBeNull(
            "the white mana is spend-restricted to creature spells (CR 106.4)");
    }

    [Fact]
    public void WhiteManaRestriction_SatisfiedByCreatureSpell_NotByNoncreature()
    {
        var land = LupinflowerVillageFactory.Create(_alice);
        var restriction = land.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Equals(ManaCost.Parse("W")))
            .SpendRestriction!;

        var creatureSpell = new Core.Spells.Spell(new Creature("Bear", "{1}{G}", 2, 2), _alice);
        var instantSpell = new Core.Spells.Spell(new Instant("Bolt", "{R}"), _alice);

        restriction.SatisfiedBy(creatureSpell).Should().BeTrue(
            "creature-spell-only mana may pay a creature spell");
        restriction.SatisfiedBy(instantSpell).Should().BeFalse(
            "creature-spell-only mana may NOT pay a noncreature spell");
    }

    // =========================================================================
    // Sac-tutor activated ability — shape
    // =========================================================================

    [Fact]
    public void HasDigActivatedAbility_With1W_Tap_Sacrifice()
    {
        var land = LupinflowerVillageFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().ContainSingle("the dig ability is the only non-mana activated ability");

        var costs = activated[0].Costs.ToList();
        costs.OfType<Core.Costs.ManaCostCost>().Should().NotBeEmpty("the dig costs {1}{W}");
        costs.OfType<Core.Costs.AdditionalCost>()
            .Should().Contain(ac => ac.Description.Contains("Sacrifice"),
                "the dig requires sacrificing the land");
    }

    // =========================================================================
    // Dig resolution
    // =========================================================================

    [Fact]
    public void Dig_RevealsFirstQualifyingCreature_RestToBottom()
    {
        var nonqualifier = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var bird = MakeCreature("Hopeful Vigil Bird", CardSubtype.Bird);
        var land1 = new Land("Plains", supertypes: null, subtypes: null);
        var land2 = new Land("Forest", supertypes: null, subtypes: null);
        SeedLibrary(nonqualifier, bird, land1, land2);

        var effects = LupinflowerVillageFactory.BuildDigEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bird);
        _alice.Zones.Library.GetCards().Should().NotContain(bird);
        _alice.Zones.Library.GetCards().Should().HaveCount(3);
    }

    [Fact]
    public void Dig_RevealsBatBirdMouseOrRabbit()
    {
        foreach (var subtype in new[]
        {
            CardSubtype.Bat, CardSubtype.Bird, CardSubtype.Mouse, CardSubtype.Rabbit,
        })
        {
            var alice = new Player("A", 20);
            var critter = new Creature("Critter", "{1}", 1, 1, supertypes: null,
                subtypes: new[] { subtype });
            critter.SetOwner(alice);
            alice.Zones.Library.AddCard(critter);
            critter.SetZone(Core.Zones.ZoneType.Library);

            foreach (var e in LupinflowerVillageFactory.BuildDigEffect(alice)) e.Execute();

            alice.Zones.Hand.GetCards().Should().ContainSingle()
                .Which.Should().BeSameAs(critter, $"a {subtype} card qualifies");
        }
    }

    [Fact]
    public void Dig_NoQualifier_NothingRevealed_AllBottomed()
    {
        var c1 = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var c2 = new Land("Forest", supertypes: null, subtypes: null);
        SeedLibrary(c1, c2);

        foreach (var e in LupinflowerVillageFactory.BuildDigEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Dig_OnlyLooksAtTopSix()
    {
        // Six non-qualifiers on top, a Rabbit as the 7th — below the top six,
        // so it must NOT be revealed.
        var top = new List<ICard>();
        for (var i = 0; i < 6; i++)
            top.Add(new Creature($"Bear{i}", "{1}{G}", 2, 2));
        var deepRabbit = MakeCreature("Deep Rabbit", CardSubtype.Rabbit);
        top.Add(deepRabbit);
        SeedLibrary(top.ToArray());

        foreach (var e in LupinflowerVillageFactory.BuildDigEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the only Rabbit is the 7th card, below the top six");
        _alice.Zones.Library.GetCards().Should().Contain(deepRabbit);
    }

    [Fact]
    public void Dig_EmptyLibrary_NoOp()
    {
        foreach (var e in LupinflowerVillageFactory.BuildDigEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Dig_DecliningSelector_RevealsNothing()
    {
        // CR 116.1b — "you may" opt-out: a selector that bottoms everything.
        var rabbit = MakeCreature("Rabbit", CardSubtype.Rabbit);
        var land = new Land("Forest", supertypes: null, subtypes: null);
        SeedLibrary(rabbit, land);

        var effects = LupinflowerVillageFactory.BuildDigEffect(
            _alice,
            selector: peeked => (Array.Empty<ICard>(), peeked));
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty("the declining selector revealed nothing");
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
    }
}
