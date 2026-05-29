using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Allosaurus Rider (Coldsnap, {5}{G}{G}).
///
/// Creature — Elf Warrior
/// Oracle:
///   "You may exile two green cards from your hand rather than pay this
///    spell's mana cost.
///    Allosaurus Rider's power and toughness are each equal to 1 plus
///    the number of lands you control."
///
/// Validates:
///   - Card identity: Elf Warrior, {5}{G}{G}, MV 7, green.
///   - NamedCardFactory dispatch.
///   - Layer 7a CDA: P/T = (1 + lands controlled) both axes.
///   - 5 lands → 6/6; 0 lands → 1/1.
///   - P/T updates live as lands change (CDA re-evaluates on Compute).
///   - Alt-cost: ExileTwoColoredCardsAlternativeCost(Green) — CanCastFor /
///     rejection tests.
/// </summary>
public class AllosaurusRiderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public AllosaurusRiderTests()
    {
        _zones = new ZoneService(_bus);
    }

    // ── Card identity ────────────────────────────────────────────────────

    [Fact]
    public void AllosaurusRider_IsElfWarrior_At5GG()
    {
        var rider = AllosaurusRiderFactory.Create(_alice);

        rider.Name.Should().Be("Allosaurus Rider");
        rider.ManaCost.Should().Be("{5}{G}{G}");
        rider.HasType(CardType.Creature).Should().BeTrue();
        rider.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        rider.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        rider.Owner.Should().BeSameAs(_alice);
        rider.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AllosaurusRider_IsGreen()
    {
        var rider = AllosaurusRiderFactory.Create(_alice);

        CardColors.GetColors(rider).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AllosaurusRider()
    {
        var card = NamedCardFactory.Create("Allosaurus Rider", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Allosaurus Rider");
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    // ── Layer 7a CDA: P/T = 1 + lands ───────────────────────────────────

    private Creature WireRider(Player owner)
    {
        var rider = AllosaurusRiderFactory.Create(owner, _effects, _bus);
        rider.ActiveEffects = _effects;
        return rider;
    }

    private Land MakeLand(Player owner)
    {
        var land = new Land("Forest");
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    [Fact]
    public void AllosaurusRider_ZeroLands_Is_1_1()
    {
        var rider = WireRider(_alice);
        _zones.MoveCard(rider, ZoneType.Library, ZoneType.Battlefield, _alice);

        rider.Power.Should().Be(1);
        rider.Toughness.Should().Be(1);
    }

    [Fact]
    public void AllosaurusRider_FiveLands_Is_6_6()
    {
        var rider = WireRider(_alice);
        _zones.MoveCard(rider, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (var i = 0; i < 5; i++)
        {
            var land = MakeLand(_alice);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        rider.Power.Should().Be(6);
        rider.Toughness.Should().Be(6);
    }

    [Fact]
    public void AllosaurusRider_OneLand_Is_2_2()
    {
        var rider = WireRider(_alice);
        _zones.MoveCard(rider, ZoneType.Library, ZoneType.Battlefield, _alice);

        var land = MakeLand(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        rider.Power.Should().Be(2);
        rider.Toughness.Should().Be(2);
    }

    [Fact]
    public void AllosaurusRider_CdaUpdatesLive_WhenLandIsAdded()
    {
        var rider = WireRider(_alice);
        _zones.MoveCard(rider, ZoneType.Library, ZoneType.Battlefield, _alice);

        // No lands → 1/1.
        rider.Power.Should().Be(1);
        rider.Toughness.Should().Be(1);

        // Add a land — CDA re-evaluates immediately.
        var land = MakeLand(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        rider.Power.Should().Be(2);
        rider.Toughness.Should().Be(2);
    }

    [Fact]
    public void AllosaurusRider_OpponentLands_DoNotCount()
    {
        // Only lands the rider's controller controls count (CR 109.5).
        var rider = WireRider(_alice);
        _zones.MoveCard(rider, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Bob controls 3 lands; Alice controls 0.
        for (var i = 0; i < 3; i++)
        {
            var land = MakeLand(_bob);
            _bob.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        // Alice controls 0 lands → still 1/1.
        rider.Power.Should().Be(1);
        rider.Toughness.Should().Be(1);
    }

    [Fact]
    public void AllosaurusRider_CdaInactive_WhenNotOnBattlefield()
    {
        // Shape-only — no lifecycle wiring; base P/T is 0/0.
        var rider = AllosaurusRiderFactory.Create(_alice);

        // Off-battlefield — CDA gate returns false; base values exposed.
        rider.BasePower.Should().Be(0);
        rider.BaseToughness.Should().Be(0);
    }

    // ── Alt-cost: exile two green cards from hand ────────────────────────

    [Fact]
    public void AltCost_TwoGreenCards_CanCastFor_IsTrue()
    {
        var rider = AllosaurusRiderFactory.Create(_alice);
        rider.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rider);

        var pitch1 = new Creature("Llanowar Elves", "{G}", 1, 1);
        pitch1.SetOwner(_alice);
        pitch1.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch1);

        var pitch2 = new Sorcery("Giant Growth", "{G}");
        pitch2.SetOwner(_alice);
        pitch2.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch2);

        var cost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Green, pitch1, pitch2);

        cost.CanCastFor(rider, _alice).Should().BeTrue();
    }

    [Fact]
    public void AltCost_ExilesTwoPitchedCards_OnResolved()
    {
        var rider = AllosaurusRiderFactory.Create(_alice);

        var pitch1 = new Creature("Llanowar Elves", "{G}", 1, 1);
        pitch1.SetOwner(_alice);
        pitch1.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch1);

        var pitch2 = new Sorcery("Giant Growth", "{G}");
        pitch2.SetOwner(_alice);
        pitch2.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch2);

        var cost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Green, pitch1, pitch2);

        cost.OnResolved(rider, _alice);

        pitch1.Zone.Should().Be(ZoneType.Exile);
        pitch2.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(new ICard[] { pitch1, pitch2 });
    }

    [Fact]
    public void AltCost_RejectsNonGreenCard()
    {
        var rider = AllosaurusRiderFactory.Create(_alice);

        var greenCard = new Creature("Llanowar Elves", "{G}", 1, 1);
        greenCard.SetOwner(_alice);
        greenCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(greenCard);

        var blueCard = new Instant("Counterspell", "{U}{U}");
        blueCard.SetOwner(_alice);
        blueCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blueCard);

        var cost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Green, greenCard, blueCard);

        cost.CanCastFor(rider, _alice).Should().BeFalse(
            "both pitched cards must be green");
    }

    [Fact]
    public void AltCost_RejectsSameCardTwice()
    {
        var rider = AllosaurusRiderFactory.Create(_alice);

        var greenCard = new Creature("Llanowar Elves", "{G}", 1, 1);
        greenCard.SetOwner(_alice);
        greenCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(greenCard);

        var cost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Green, greenCard, greenCard);

        cost.CanCastFor(rider, _alice).Should().BeFalse(
            "both pitched cards must be distinct references");
    }

    [Fact]
    public void AltCost_AlternativeManaCost_IsZero()
    {
        var greenCard1 = new Creature("Llanowar Elves", "{G}", 1, 1);
        greenCard1.SetOwner(_alice);
        greenCard1.SetZone(ZoneType.Hand);

        var greenCard2 = new Creature("Elvish Mystic", "{G}", 1, 1);
        greenCard2.SetOwner(_alice);
        greenCard2.SetZone(ZoneType.Hand);

        var cost = new ExileTwoColoredCardsAlternativeCost(
            ManaColor.Green, greenCard1, greenCard2);

        cost.AlternativeManaCost.Should().Be(ManaCost.Zero,
            "exiling the two cards IS the entire cost; no mana is paid");
    }
}
