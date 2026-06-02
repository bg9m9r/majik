using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SylvanAdvocateFactory"/> (Oath of the Gatewatch,
/// {2}{G}). Creature — Elf Druid Ally 2/3. Oracle text (verified against
/// Scryfall):
///   "Vigilance
///    As long as you control six or more lands, this creature and land
///    creatures you control get +2/+2."
///
/// Covers:
/// - Identity (Elf Druid Ally, {2}{G}, 2/3, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Vigilance keyword marker (CR 702.20).
/// - Land-count gate (CR 613.7c, intervening-if-style condition): with
///   five lands the anthem is OFF; with six lands it is ON.
/// - When ON: Sylvan Advocate itself gets +2/+2 ("this creature").
/// - When ON: a land creature the controller controls gets +2/+2.
/// - A non-land creature the controller controls (other than Advocate
///   itself) is NOT buffed.
/// - The gate lifts again when the land count drops back below six.
/// </summary>
[Trait("Color", "G")]
public class SylvanAdvocateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land MakeLand(Player owner, string name)
    {
        var land = new Land(name);
        land.SetOwner(owner);
        land.SetController(owner);
        owner.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    private static void AddLands(Player owner, int count)
    {
        for (var i = 0; i < count; i++) MakeLand(owner, $"Forest {i}");
    }

    /// <summary>A land that is also a creature (Dryad Arbor, Land Creature —
    /// Forest Dryad 1/1) — the "land creatures you control" target of the
    /// anthem. Built via the real factory so it genuinely carries both the
    /// Creature and Land card types.</summary>
    private static Creature MakeLandCreature(Player owner)
    {
        var c = DryadArborFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakePlainCreature(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private Creature PlaceAdvocate(ContinuousEffectsService continuous)
    {
        var advocate = SylvanAdvocateFactory.Create(_alice, continuous);
        _alice.Zones.Battlefield.AddCard(advocate);
        advocate.SetZone(ZoneType.Battlefield);
        advocate.ActiveEffects = continuous;
        return advocate;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SylvanAdvocate_Identity_ElfDruidAlly_2_3()
    {
        var card = SylvanAdvocateFactory.Create(_alice);

        card.Name.Should().Be("Sylvan Advocate");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        card.HasSubtype(CardSubtype.Ally).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SylvanAdvocate_Dispatches_ThroughNamedFactory()
    {
        var created = NamedCardFactory.Create("Sylvan Advocate", _alice);

        created.Should().NotBeNull();
        created.Name.Should().Be("Sylvan Advocate");
        created.Should().BeAssignableTo<Creature>();
        ((Creature)created).HasSubtype(CardSubtype.Ally).Should().BeTrue();
    }

    [Fact]
    public void SylvanAdvocate_HasVigilance()
    {
        var card = SylvanAdvocateFactory.Create(_alice);
        CombatAbilities.HasVigilance(card).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Land-count gate — "As long as you control six or more lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void SylvanAdvocate_WithFiveLands_GateOff_NoBuff()
    {
        var continuous = new ContinuousEffectsService();
        AddLands(_alice, 5);
        var advocate = PlaceAdvocate(continuous);

        var chars = continuous.Compute(advocate);
        chars.Power.Should().Be(2, "only five lands — fewer than six, so the anthem is inactive");
        chars.Toughness.Should().Be(3);
    }

    [Fact]
    public void SylvanAdvocate_WithSixLands_BuffsItself()
    {
        var continuous = new ContinuousEffectsService();
        AddLands(_alice, 6);
        var advocate = PlaceAdvocate(continuous);

        var chars = continuous.Compute(advocate);
        chars.Power.Should().Be(2 + 2, "six lands → 'this creature' gets +2/+2");
        chars.Toughness.Should().Be(3 + 2);
    }

    [Fact]
    public void SylvanAdvocate_WithSixLands_BuffsControlledLandCreature()
    {
        var continuous = new ContinuousEffectsService();
        AddLands(_alice, 6);
        var landCreature = MakeLandCreature(_alice);
        landCreature.ActiveEffects = continuous;
        PlaceAdvocate(continuous);

        var chars = continuous.Compute(landCreature);
        chars.Power.Should().Be(1 + 2, "a land creature you control gets +2/+2 while the gate is on");
        chars.Toughness.Should().Be(1 + 2);
    }

    [Fact]
    public void SylvanAdvocate_DoesNotBuff_NonLandCreature()
    {
        var continuous = new ContinuousEffectsService();
        AddLands(_alice, 6);
        var bears = MakePlainCreature(_alice);
        bears.ActiveEffects = continuous;
        PlaceAdvocate(continuous);

        var chars = continuous.Compute(bears);
        chars.Power.Should().Be(2, "Grizzly Bears is not a land and is not the Advocate — unaffected");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void SylvanAdvocate_DoesNotBuff_OpponentLandCreature()
    {
        var continuous = new ContinuousEffectsService();
        AddLands(_alice, 6);
        var bobLandCreature = MakeLandCreature(_bob);
        bobLandCreature.ActiveEffects = continuous;
        PlaceAdvocate(continuous);

        var chars = continuous.Compute(bobLandCreature);
        chars.Power.Should().Be(1, "'land creatures you control' is controller-scoped — Bob's is unaffected");
        chars.Toughness.Should().Be(1);
    }

    [Fact]
    public void SylvanAdvocate_GateLifts_WhenLandsDropBelowSix()
    {
        var continuous = new ContinuousEffectsService();
        var lands = new List<Land>();
        for (var i = 0; i < 6; i++) lands.Add(MakeLand(_alice, $"Forest {i}"));
        var advocate = PlaceAdvocate(continuous);

        // Six lands: buffed.
        continuous.Compute(advocate).Power.Should().Be(4);

        // Remove one land → five remain → gate lifts. In production a
        // CardMovedEvent bumps the layer-cache generation; here the removed
        // land has no ActiveEffects link, so invalidate the cache explicitly
        // to mirror that event-driven recomputation.
        var removed = lands[0];
        _alice.Zones.Battlefield.RemoveCard(removed);
        removed.SetZone(ZoneType.Graveyard);
        continuous.Clear();

        var chars = continuous.Compute(advocate);
        chars.Power.Should().Be(2, "land count fell to five — the anthem switches off");
        chars.Toughness.Should().Be(3);
    }
}
