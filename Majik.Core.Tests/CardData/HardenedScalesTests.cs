using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HardenedScalesFactory"/>.
///
/// Card: Hardened Scales — Enchantment {G} (Magic Origins).
///   "If one or more +1/+1 counters would be put on a creature you
///    control, that many plus one +1/+1 counters are put on it instead."
///
/// Covers:
///   - Identity / dispatch.
///   - ETB-counter intent (Strangleroot Geist-style) bump: 1 → 2; 2 → 3.
///   - Direct CounterAddIntent bump via CountersService.Add.
///   - Stacks across two Hardened Scales (+2 total).
///   - Scoping — opponent's creature isn't bumped; -1/-1 counters aren't bumped.
///   - Inert while not on battlefield.
///   - Single-arg create (no bus) is shape-only.
/// </summary>
public class HardenedScalesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HardenedScales_Identity()
    {
        var c = HardenedScalesFactory.Create(_alice);

        c.Name.Should().Be("Hardened Scales");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HardenedScales()
    {
        var card = NamedCardFactory.Create("Hardened Scales", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Hardened Scales");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{G}");
    }

    // -----------------------------------------------------------------------
    // ETB-counter intent — bumps PlusOneCountersOnEnter
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbIntent_OnControlledCreature_BumpsByOne()
    {
        var bus = new ReplacementBus();
        var scales = HardenedScalesFactory.Create(_alice, bus);
        PlaceOnBattlefield(scales, _alice);

        var geist = new Creature("Strangleroot Geist", "{G}{G}", 2, 1);
        geist.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            Card: geist,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice,
            PlusOneCountersOnEnter: 1);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.PlusOneCountersOnEnter.Should().Be(2,
            "1 ETB counter + Hardened Scales = 2");
    }

    [Fact]
    public void EtbIntent_WithThreeCounters_BumpsToFour()
    {
        var bus = new ReplacementBus();
        var scales = HardenedScalesFactory.Create(_alice, bus);
        PlaceOnBattlefield(scales, _alice);

        var triskelion = new Creature("Triskelion", "{6}", 1, 1);
        triskelion.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            triskelion, ZoneType.Hand, ZoneType.Battlefield,
            _alice, PlusOneCountersOnEnter: 3);

        var result = bus.Apply(intent);

        result!.PlusOneCountersOnEnter.Should().Be(4, "3 + 1 = 4");
    }

    [Fact]
    public void EtbIntent_TwoHardenedScales_StackForPlusTwo()
    {
        var bus = new ReplacementBus();
        PlaceOnBattlefield(HardenedScalesFactory.Create(_alice, bus), _alice);
        PlaceOnBattlefield(HardenedScalesFactory.Create(_alice, bus), _alice);

        var geist = new Creature("Strangleroot Geist", "{G}{G}", 2, 1);
        geist.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            geist, ZoneType.Hand, ZoneType.Battlefield,
            _alice, PlusOneCountersOnEnter: 1);

        var result = bus.Apply(intent);

        result!.PlusOneCountersOnEnter.Should().Be(3,
            "1 + 1 (first Scales) + 1 (second Scales) = 3 — CR 616.1c, each fires once");
    }

    [Fact]
    public void EtbIntent_WithZeroCounters_NotBumped()
    {
        var bus = new ReplacementBus();
        var scales = HardenedScalesFactory.Create(_alice, bus);
        PlaceOnBattlefield(scales, _alice);

        var vanilla = new Creature("Vanilla Bear", "{1}{G}", 2, 2);
        vanilla.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            vanilla, ZoneType.Hand, ZoneType.Battlefield,
            _alice, PlusOneCountersOnEnter: 0);

        var result = bus.Apply(intent);

        result!.PlusOneCountersOnEnter.Should().Be(0,
            "printed text requires 'one or more' counters to fire");
    }

    [Fact]
    public void EtbIntent_OpponentCreature_NotBumped()
    {
        var bus = new ReplacementBus();
        var scales = HardenedScalesFactory.Create(_alice, bus);
        PlaceOnBattlefield(scales, _alice);

        var bobGeist = new Creature("Strangleroot Geist", "{G}{G}", 2, 1);
        bobGeist.SetOwner(_bob);

        var intent = new ZoneMoveIntent(
            bobGeist, ZoneType.Hand, ZoneType.Battlefield,
            _bob, PlusOneCountersOnEnter: 1);

        var result = bus.Apply(intent);

        result!.PlusOneCountersOnEnter.Should().Be(1,
            "Hardened Scales is one-sided ('a creature you control')");
    }

    // -----------------------------------------------------------------------
    // Direct CounterAddIntent via CountersService
    // -----------------------------------------------------------------------

    [Fact]
    public void CountersService_OnControlledCreature_BumpsByOne()
    {
        var bus = new ReplacementBus();
        var scales = HardenedScalesFactory.Create(_alice, bus);
        PlaceOnBattlefield(scales, _alice);

        var bear = new Creature("Champion of the Parish", "{W}", 1, 1);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(2, "1 requested + 1 from Hardened Scales");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void CountersService_MinusOneMinusOne_NotBumped()
    {
        var bus = new ReplacementBus();
        var scales = HardenedScalesFactory.Create(_alice, bus);
        PlaceOnBattlefield(scales, _alice);

        var bear = new Creature("Some Creature", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bear, CounterType.MinusOneMinusOne, 1, bus);

        placed.Should().Be(1, "Hardened Scales scopes to +1/+1 counters only");
        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void CountersService_NoBus_FallsThroughToDirectAdd()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 2, replacements: null);

        placed.Should().Be(2);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Lifecycle — off battlefield, replacement inert
    // -----------------------------------------------------------------------

    [Fact]
    public void HardenedScales_InHand_DoesNotBump()
    {
        var bus = new ReplacementBus();
        var scales = HardenedScalesFactory.Create(_alice, bus);
        // Don't place on battlefield — leave Zone defaulted (Library/Hand).

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);
        placed.Should().Be(1, "Hardened Scales must be on the battlefield to fire");
    }

    // -----------------------------------------------------------------------
    // Single-arg shape factory: no bus → no registration
    // -----------------------------------------------------------------------

    [Fact]
    public void SingleArgFactory_DoesNotRegisterReplacement()
    {
        // No throw; no bus to register against. Just verifies the
        // single-arg overload returns a clean card.
        var scales = HardenedScalesFactory.Create(_alice);
        scales.Should().NotBeNull();
        scales.Name.Should().Be("Hardened Scales");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Enchantment scales, Player owner)
    {
        owner.Zones.Battlefield.AddCard(scales);
        scales.SetZone(ZoneType.Battlefield);
    }
}
