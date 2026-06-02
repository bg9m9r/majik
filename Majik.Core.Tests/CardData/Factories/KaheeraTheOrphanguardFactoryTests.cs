using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KaheeraTheOrphanguardFactory"/> (Ikoria,
/// {1}{G/W}{G/W}). Legendary Creature — Cat Beast 3/2. Oracle text
/// (verified against Scryfall):
///   "Companion — Each creature card in your starting deck is a Cat,
///    Elemental, Nightmare, Dinosaur, or Beast card.
///    Vigilance
///    Each other creature you control that's a Cat, Elemental, Nightmare,
///    Dinosaur, or Beast gets +1/+1 and has vigilance."
///
/// Covers:
/// - Identity (Legendary, Cat Beast, hybrid {1}{G/W}{G/W} cost, 3/2,
///   owner/controller).
/// - Vigilance keyword marker on Kaheera itself (CR 702.20).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - The anthem buffs OTHER matching creatures (Cat / Elemental /
///   Nightmare / Dinosaur / Beast) you control by +1/+1 and grants
///   vigilance.
/// - Kaheera itself is NOT buffed by its own anthem ("Each OTHER").
/// - A non-matching creature you control is NOT buffed.
/// - An opponent's matching creature is NOT buffed ("you control").
/// - The companion deck-construction restriction (CR 702.139) accepts a
///   legal starting deck and rejects an off-type creature card.
/// </summary>
[Trait("Color", "GW")]
public class KaheeraTheOrphanguardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name, params CardSubtype[] subtypes)
    {
        var c = new Creature(name, "{1}", 1, 1, subtypes: subtypes);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Kaheera_Identity_LegendaryCatBeast_3_2_HybridCost()
    {
        var card = KaheeraTheOrphanguardFactory.Create(_alice);

        card.Name.Should().Be("Kaheera, the Orphanguard");
        card.ManaCost.Should().Be("{1}{G/W}{G/W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Kaheera_HasVigilanceKeyword()
    {
        var card = KaheeraTheOrphanguardFactory.Create(_alice);

        card.Abilities.OfType<Majik.Core.Abilities.KeywordAbility>()
            .Select(ka => ka.Keyword)
            .Should().Contain("Vigilance");
    }

    [Fact]
    public void Kaheera_Dispatches_ThroughNamedFactory()
    {
        var created = NamedCardFactory.Create("Kaheera, the Orphanguard", _alice);

        created.Should().NotBeNull();
        created.Name.Should().Be("Kaheera, the Orphanguard");
        created.Should().BeAssignableTo<Creature>();
        ((Creature)created).HasSubtype(CardSubtype.Cat).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Anthem — "Each other creature you control that's a Cat, Elemental,
    // Nightmare, Dinosaur, or Beast gets +1/+1 and has vigilance."
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(CardSubtype.Cat)]
    [InlineData(CardSubtype.Elemental)]
    [InlineData(CardSubtype.Nightmare)]
    [InlineData(CardSubtype.Dinosaur)]
    [InlineData(CardSubtype.Beast)]
    public void Kaheera_Buffs_MatchingCreatureYouControl(CardSubtype subtype)
    {
        var continuous = new ContinuousEffectsService();

        var ally = MakeCreature(_alice, "Ally", subtype);
        ally.ActiveEffects = continuous;

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(kaheera);
        kaheera.SetZone(ZoneType.Battlefield);
        kaheera.ActiveEffects = continuous;

        var chars = continuous.Compute(ally);
        chars.Power.Should().Be(2, "1/1 base + Kaheera anthem +1/+1");
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().Contain("Vigilance");
    }

    [Fact]
    public void Kaheera_DoesNotBuffItself()
    {
        var continuous = new ContinuousEffectsService();

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(kaheera);
        kaheera.SetZone(ZoneType.Battlefield);
        kaheera.ActiveEffects = continuous;

        // "Each OTHER creature" — Kaheera does not pump itself.
        var chars = continuous.Compute(kaheera);
        chars.Power.Should().Be(3, "Kaheera's anthem excludes itself");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void Kaheera_DoesNotBuff_NonMatchingCreature()
    {
        var continuous = new ContinuousEffectsService();

        var soldier = MakeCreature(_alice, "Soldier", CardSubtype.Soldier);
        soldier.ActiveEffects = continuous;

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(kaheera);
        kaheera.SetZone(ZoneType.Battlefield);
        kaheera.ActiveEffects = continuous;

        var chars = continuous.Compute(soldier);
        chars.Power.Should().Be(1, "Soldier is not a Cat/Elemental/Nightmare/Dinosaur/Beast");
        chars.Toughness.Should().Be(1);
        chars.Keywords.Should().NotContain("Vigilance");
    }

    [Fact]
    public void Kaheera_DoesNotBuff_OpponentMatchingCreature()
    {
        var continuous = new ContinuousEffectsService();

        var bobCat = MakeCreature(_bob, "Bob's Cat", CardSubtype.Cat);
        bobCat.ActiveEffects = continuous;

        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(kaheera);
        kaheera.SetZone(ZoneType.Battlefield);
        kaheera.ActiveEffects = continuous;

        // "you control" — Bob's Cat is NOT buffed.
        var chars = continuous.Compute(bobCat);
        chars.Power.Should().Be(1, "the anthem is controller-scoped (\"you control\")");
        chars.Toughness.Should().Be(1);
        chars.Keywords.Should().NotContain("Vigilance");
    }

    // -----------------------------------------------------------------------
    // Companion deck-construction restriction (CR 702.139)
    // -----------------------------------------------------------------------

    [Fact]
    public void CompanionRestriction_Accepts_AllMatchingCreatureCards()
    {
        var deck = new ICard[]
        {
            new Creature("Cat", "{1}", 1, 1, subtypes: new[] { CardSubtype.Cat }),
            new Creature("Beast", "{2}", 2, 2, subtypes: new[] { CardSubtype.Beast }),
            new Creature("Elemental", "{3}", 3, 3, subtypes: new[] { CardSubtype.Elemental }),
            // Non-creature cards are unconstrained by the companion rule.
            new Majik.Core.Cards.Instant("Bolt", "{R}"),
        };

        KaheeraTheOrphanguardFactory.CompanionRestriction
            .IsSatisfiedBy(deck).Should().BeTrue();
    }

    [Fact]
    public void CompanionRestriction_Rejects_OffTypeCreatureCard()
    {
        var deck = new ICard[]
        {
            new Creature("Cat", "{1}", 1, 1, subtypes: new[] { CardSubtype.Cat }),
            new Creature("Goblin", "{R}", 1, 1, subtypes: new[] { CardSubtype.Goblin }),
        };

        KaheeraTheOrphanguardFactory.CompanionRestriction
            .IsSatisfiedBy(deck).Should().BeFalse();
    }

    [Fact]
    public void CompanionValidator_Surfaces_Restriction()
    {
        var kaheera = KaheeraTheOrphanguardFactory.Create(_alice);
        var deck = new ICard[]
        {
            new Creature("Goblin", "{R}", 1, 1, subtypes: new[] { CardSubtype.Goblin }),
        };

        var result = CompanionValidator.Validate(
            kaheera, KaheeraTheOrphanguardFactory.CompanionRestriction, deck);

        result.IsValid.Should().BeFalse();
    }
}
