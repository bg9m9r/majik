using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MarkovBaronFactory"/>.
///
/// Markov Baron (Duskmourn, {2}{B}). Creature — Vampire Noble 2/2. Oracle
/// (verified against Scryfall):
///   "Convoke (…)
///    Lifelink
///    Other Vampires you control get +1/+1.
///    Madness {2}{B} (…)"
///
/// Coverage (unique behaviour — the contract test asserts dispatch +
/// well-formedness):
/// - Identity (name, cost, Vampire + Noble subtypes, P/T, Black colour).
/// - Lifelink (intrinsic keyword from the JSON).
/// - Convoke keyword marker (CR 702.51) — keys the engine-side cast prompt.
/// - Lord static (CR 613.7c): other controller-Vampires get +1/+1; the Baron
///   doesn't buff itself ("Other"); opponents' / non-Vampires unaffected.
/// - Madness {2}{B} catalogued (CR 702.35 — supported intrinsically).
/// </summary>
[Trait("Color", "M")]
public class MarkovBaronFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeVampire(Player owner, string name = "Vampire Nighthawk")
    {
        var c = new Creature(name, "{1}{B}{B}", 2, 3, subtypes: new[] { CardSubtype.Vampire });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void MarkovBaron_Identity()
    {
        var c = MarkovBaronFactory.Create(_alice);

        c.Name.Should().Be("Markov Baron");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.Black);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MarkovBaron_HasLifelink()
    {
        var c = MarkovBaronFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Lifelink", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Markov Baron has Lifelink (CR 702.15).");
    }

    [Fact]
    public void MarkovBaron_CarriesConvokeMarker()
    {
        var c = MarkovBaronFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Convoke", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the Convoke marker (CR 702.51) keys the engine-side cast prompt.");
    }

    [Fact]
    public void MarkovBaron_BuffsOtherControllerVampire_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherVamp = MakeVampire(_alice);
        otherVamp.ActiveEffects = svc;

        var baron = MarkovBaronFactory.Create(_alice, svc);
        baron.SetZone(ZoneType.Battlefield);
        baron.ActiveEffects = svc;

        otherVamp.GetPower().Should().Be(3);
        otherVamp.GetToughness().Should().Be(4);
    }

    [Fact]
    public void MarkovBaron_DoesNotBuffItself()
    {
        var svc = new ContinuousEffectsService();

        var baron = MarkovBaronFactory.Create(_alice, svc);
        baron.SetZone(ZoneType.Battlefield);
        baron.ActiveEffects = svc;

        baron.GetPower().Should().Be(2, "the printed 'Other' excludes the Baron from its own buff.");
        baron.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MarkovBaron_DoesNotBuff_OpponentVampire()
    {
        var svc = new ContinuousEffectsService();

        var oppVamp = MakeVampire(_bob);
        oppVamp.ActiveEffects = svc;

        var baron = MarkovBaronFactory.Create(_alice, svc);
        baron.SetZone(ZoneType.Battlefield);
        baron.ActiveEffects = svc;

        oppVamp.GetPower().Should().Be(2, "controller-scoped anthem (CR 109.5).");
    }

    [Fact]
    public void MarkovBaron_Madness_IsCatalogued()
    {
        // CR 702.35 — madness is supported intrinsically via MadnessCatalog;
        // Markov Baron's {2}{B} madness cost must be present.
        var baron = MarkovBaronFactory.Create(_alice);
        MadnessCatalog.HasMadness(baron).Should().BeTrue();
        MadnessCatalog.CostFor(baron).Should().Be(ManaCost.Parse("{2}{B}"));
    }
}
