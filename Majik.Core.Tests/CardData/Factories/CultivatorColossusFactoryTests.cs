using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CultivatorColossusFactory"/> (The Brothers' War,
/// {4}{G}{G}{G}).
///
/// Creature — Plant Beast. Oracle text:
///   "Trample
///    Cultivator Colossus's power and toughness are each equal to the number
///    of lands you control.
///    When this creature enters, you may put a land card from your hand onto
///    the battlefield tapped. If you do, draw a card and repeat this process."
///
/// Covers:
/// - Identity ({4}{G}{G}{G} Creature, Plant + Beast subtypes, green, MV 7).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Trample keyword marker (CR 702.19).
/// - Layer 7a CDA: P/T = number of lands the controller controls
///   (3 lands → 3/3; 0 lands → 0/0; opponent lands don't count).
/// - ETB loop: puts each land from hand onto the battlefield TAPPED, drawing
///   a card after each, until a land drawn is the next chain link or the hand
///   runs dry (CR 113.6c / CR 121.1).
/// - ETB with no land in hand → clean no-op.
/// - ETB chains through a freshly-drawn land (draw happens before the next
///   "put a land").
/// </summary>
[Trait("Color", "G")]
public class CultivatorColossusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public CultivatorColossusFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void CultivatorColossus_Identity()
    {
        var c = CultivatorColossusFactory.Create(_alice);

        c.Name.Should().Be("Cultivator Colossus");
        c.ManaCost.Should().Be("{4}{G}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CultivatorColossus_IsGreen()
    {
        var c = CultivatorColossusFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void CultivatorColossus_ManaValue_IsSeven()
    {
        var c = CultivatorColossusFactory.Create(_alice);

        // {4}{G}{G}{G} = mana value 7 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(7, "CR 202.3 — {4}{G}{G}{G} has mana value 7");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CultivatorColossus()
    {
        var card = NamedCardFactory.Create("Cultivator Colossus", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Cultivator Colossus");
        card.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
    }

    [Fact]
    public void CultivatorColossus_HasTrample()
    {
        var c = CultivatorColossusFactory.Create(_alice);

        // CR 702.19 — Trample present as a KeywordAbility marker.
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Trample");
    }

    [Fact]
    public void CultivatorColossus_HasExactlyOneEtbTrigger()
    {
        var c = CultivatorColossusFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "Cultivator Colossus has exactly one ETB triggered ability");
    }

    // ── Layer 7a CDA: P/T = number of lands ──────────────────────────────

    private Creature WireColossus(Player owner)
    {
        var c = CultivatorColossusFactory.Create(
            owner, _effects, zoneService: null, eventBus: _bus, triggers: null);
        c.ActiveEffects = _effects;
        return c;
    }

    private Land MakeLand(Player owner)
    {
        var land = new Land("Forest");
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    [Fact]
    public void CultivatorColossus_ZeroLands_Is_0_0()
    {
        var c = WireColossus(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        // The Colossus itself is a creature, not a land — it does not count.
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(0);
    }

    [Fact]
    public void CultivatorColossus_ThreeLands_Is_3_3()
    {
        var c = WireColossus(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (var i = 0; i < 3; i++)
        {
            var land = MakeLand(_alice);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void CultivatorColossus_CdaUpdatesLive_WhenLandAdded()
    {
        var c = WireColossus(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        c.Power.Should().Be(0);

        var land = MakeLand(_alice);
        land.ActiveEffects = c.ActiveEffects;
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
    }

    [Fact]
    public void CultivatorColossus_OpponentLands_DoNotCount()
    {
        // Only lands the controller controls count (CR 109.5).
        var c = WireColossus(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (var i = 0; i < 4; i++)
        {
            var land = MakeLand(_bob);
            _bob.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        c.Power.Should().Be(0, "only Alice's lands count toward her Colossus's P/T");
        c.Toughness.Should().Be(0);
    }

    // ── ETB loop: put land tapped, draw, repeat ──────────────────────────

    [Fact]
    public void Etb_PutsEachLandFromHandTapped_DrawingAfterEach()
    {
        // Hand: two lands. Library: two nonlands so each draw pulls a nonland
        // (which can't continue the chain). Expected: both lands enter tapped,
        // two cards drawn, then the loop stops (no land left in hand).
        var forest1 = new Land("Forest");
        forest1.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest1);
        forest1.SetZone(ZoneType.Hand);

        var forest2 = new Land("Forest");
        forest2.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest2);
        forest2.SetZone(ZoneType.Hand);

        var bolt1 = new Instant("Lightning Bolt", "{R}");
        bolt1.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt1);
        bolt1.SetZone(ZoneType.Library);

        var bolt2 = new Instant("Lightning Bolt", "{R}");
        bolt2.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt2);
        bolt2.SetZone(ZoneType.Library);

        var effects = CultivatorColossusFactory.BuildEtbEffect(_alice, zoneService: null);
        foreach (var e in effects) e.Execute();

        forest1.Zone.Should().Be(ZoneType.Battlefield);
        forest2.Zone.Should().Be(ZoneType.Battlefield);
        forest1.IsTapped.Should().BeTrue("the land enters the battlefield tapped");
        forest2.IsTapped.Should().BeTrue("the land enters the battlefield tapped");

        // Two lands placed → two draws.
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { bolt1, bolt2 },
            "a card is drawn after each land is put onto the battlefield");
        _alice.Zones.Library.GetCards().Should().BeEmpty("both library cards were drawn");
        _alice.Zones.Battlefield.GetCards().Should().Contain(new ICard[] { forest1, forest2 });
    }

    [Fact]
    public void Etb_NoLandInHand_IsCleanNoOp()
    {
        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(counterspell);
        counterspell.SetZone(ZoneType.Hand);

        var effects = CultivatorColossusFactory.BuildEtbEffect(_alice, zoneService: null);
        var resolve = () => { foreach (var e in effects) e.Execute(); };

        resolve.Should().NotThrow("no land in hand → the optional ETB loop is a clean no-op");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(counterspell);
    }

    [Fact]
    public void Etb_ChainsThroughFreshlyDrawnLand()
    {
        // Hand starts with one land. Library top is another land, so the draw
        // after placing the first land hands the loop its next link. Expected:
        // both lands end up on the battlefield tapped; the loop terminates when
        // the third draw finds an empty library / nonland.
        var landInHand = new Land("Forest");
        landInHand.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(landInHand);
        landInHand.SetZone(ZoneType.Hand);

        // Drawn first → becomes the next chain link.
        var landInLibrary = new Land("Forest");
        landInLibrary.SetOwner(_alice);
        _alice.Zones.Library.AddCard(landInLibrary);
        landInLibrary.SetZone(ZoneType.Library);

        var effects = CultivatorColossusFactory.BuildEtbEffect(_alice, zoneService: null);
        foreach (var e in effects) e.Execute();

        landInHand.Zone.Should().Be(ZoneType.Battlefield);
        landInLibrary.Zone.Should().Be(ZoneType.Battlefield,
            "the land drawn by the loop is a candidate for the next iteration");
        landInHand.IsTapped.Should().BeTrue();
        landInLibrary.IsTapped.Should().BeTrue();
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(2);
    }

    [Fact]
    public void Etb_EmptyLibraryAfterPlacingLand_StampsLossFlag_DoesNotThrow()
    {
        // One land in hand, empty library. Place the land tapped, then "draw a
        // card" from an empty library stamps the CR 704.5b loss flag (no throw).
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var effects = CultivatorColossusFactory.BuildEtbEffect(_alice, zoneService: null);
        var resolve = () => { foreach (var e in effects) e.Execute(); };

        resolve.Should().NotThrow();
        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeTrue();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library stamps the CR 704.5b loss flag");
    }
}
