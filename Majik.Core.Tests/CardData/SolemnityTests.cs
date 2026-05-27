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
/// Unit tests for <see cref="SolemnityFactory"/>.
///
/// Card: Solemnity — Enchantment {2}{W} (Hour of Devotion).
///   "Players can't get counters. Permanents enter the battlefield without
///    counters. If a counter would be put on a permanent or player, it
///    isn't."
///
/// Covers:
///   - Identity / dispatch.
///   - CounterAddIntent rewritten to Amount = 0 (all counter types, any controller).
///   - CountersService.Add returns 0 / commits nothing while Solemnity is in play.
///   - ETB ZoneMoveIntent with PlusOneCountersOnEnter > 0 reset to 0.
///   - Symmetric — opponent's counters are silenced too.
///   - LTB: replacement inert while Solemnity is off battlefield.
///   - Shape-only Create(Player) does not register.
/// </summary>
public class SolemnityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------- Identity ---------------------------------------------------

    [Fact]
    public void Solemnity_Identity()
    {
        var c = SolemnityFactory.Create(_alice);

        c.Name.Should().Be("Solemnity");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Solemnity()
    {
        var card = NamedCardFactory.Create("Solemnity", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Solemnity");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{W}");
    }

    // -------- CounterAddIntent --------------------------------------------

    [Fact]
    public void CounterAddIntent_PlusOnePlusOne_RewrittenToZero()
    {
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        PlaceOnBattlefield(solemnity, _alice);

        var bear = PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 3, bus);

        placed.Should().Be(0, "Solemnity silences every counter placement");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void CounterAddIntent_MinusOneMinusOne_RewrittenToZero()
    {
        // Solemnity is the Vizier-of-Remedies super-set — type-agnostic.
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        PlaceOnBattlefield(solemnity, _alice);

        var bear = PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        var placed = CountersService.Add(bear, CounterType.MinusOneMinusOne, 5, bus);

        placed.Should().Be(0,
            "Solemnity hits -1/-1 counters too (Hapatra / Soul-Scar combo) — full type-agnostic stop");
        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public void CounterAddIntent_CounterOnOpponentsPermanent_AlsoSilenced()
    {
        // Solemnity is symmetric — opponent's counters are silenced too.
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        PlaceOnBattlefield(solemnity, _alice);

        var bobBear = PlaceCreatureOnBattlefield("Goblin", "{R}", 1, 1, _bob);

        var placed = CountersService.Add(bobBear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(0,
            "Solemnity is symmetric — opponent's counter placements are also silenced");
        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void CounterAddIntent_DirectBusApply_ReturnsZeroAmount()
    {
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        PlaceOnBattlefield(solemnity, _alice);

        var bear = PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        var intent = new CounterAddIntent(bear, CounterType.PlusOnePlusOne, 4);
        var replaced = bus.Apply(intent);

        replaced.Should().NotBeNull();
        replaced!.Amount.Should().Be(0, "Solemnity rewrites Amount to 0");
    }

    // -------- ETB ZoneMoveIntent -----------------------------------------

    [Fact]
    public void EtbIntent_PlusOneCountersOnEnter_StrippedToZero()
    {
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        PlaceOnBattlefield(solemnity, _alice);

        var triskelion = new Creature("Triskelion", "{6}", 1, 1);
        triskelion.SetOwner(_alice);

        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            Card: triskelion,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice,
            PlusOneCountersOnEnter: 3);

        var replaced = bus.Apply(intent);

        replaced.Should().NotBeNull();
        replaced!.PlusOneCountersOnEnter.Should().Be(0,
            "Solemnity strips the ETB-counter rider — Triskelion enters as 1/1, no counters");
    }

    [Fact]
    public void EtbIntent_StrangleRootGeist_StrippedToZero()
    {
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        PlaceOnBattlefield(solemnity, _alice);

        var geist = new Creature("Strangleroot Geist", "{G}{G}", 2, 1);
        geist.SetOwner(_alice);

        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            geist, ZoneType.Hand, ZoneType.Battlefield, _alice,
            PlusOneCountersOnEnter: 1);

        var replaced = bus.Apply(intent);

        replaced!.PlusOneCountersOnEnter.Should().Be(0);
    }

    [Fact]
    public void EtbIntent_ZeroCounters_LeftAlone()
    {
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        PlaceOnBattlefield(solemnity, _alice);

        var vanilla = new Creature("Vanilla Bear", "{1}{G}", 2, 2);
        vanilla.SetOwner(_alice);

        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            vanilla, ZoneType.Hand, ZoneType.Battlefield, _alice,
            PlusOneCountersOnEnter: 0);

        var replaced = bus.Apply(intent);

        replaced!.PlusOneCountersOnEnter.Should().Be(0,
            "no counters to begin with — Solemnity is a no-op (Applies short-circuits)");
    }

    [Fact]
    public void EtbIntent_ToGraveyard_NotStrippedByZoneGuard()
    {
        // ToZone != Battlefield — should not trigger Solemnity's ETB clause.
        // (The clause is for "permanents enter the battlefield", not arbitrary moves.)
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        PlaceOnBattlefield(solemnity, _alice);

        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);

        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            creature, ZoneType.Battlefield, ZoneType.Graveyard, _alice,
            PlusOneCountersOnEnter: 2);

        var replaced = bus.Apply(intent);

        // Non-ETB moves don't carry meaningful PlusOneCountersOnEnter
        // semantics — Solemnity's Applies short-circuits on ToZone gate so
        // the field is left as-is.
        replaced!.PlusOneCountersOnEnter.Should().Be(2,
            "ToZone is Graveyard — Solemnity's ETB clause doesn't fire");
    }

    // -------- Lifecycle --------------------------------------------------

    [Fact]
    public void SolemnityNotOnBattlefield_CounterAdd_FiresNormally()
    {
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        // Don't place — leave in default (Library) zone.

        var bear = PlaceCreatureOnBattlefield("Bear", "{1}{G}", 2, 2, _alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 2, bus);

        placed.Should().Be(2,
            "Solemnity must be on the battlefield to silence (CR 614.6)");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void SolemnityNotOnBattlefield_EtbIntent_PassesThrough()
    {
        var bus = new ReplacementBus();
        var solemnity = SolemnityFactory.Create(_alice, bus);
        // Off battlefield.

        var triskelion = new Creature("Triskelion", "{6}", 1, 1);
        triskelion.SetOwner(_alice);

        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            triskelion, ZoneType.Hand, ZoneType.Battlefield, _alice,
            PlusOneCountersOnEnter: 3);

        var replaced = bus.Apply(intent);

        replaced!.PlusOneCountersOnEnter.Should().Be(3,
            "Solemnity off battlefield → ETB-counter rider survives");
    }

    // -------- Shape-only factory does not register -----------------------

    [Fact]
    public void SingleArgFactory_DoesNotRegisterReplacements()
    {
        var s = SolemnityFactory.Create(_alice);
        s.Should().NotBeNull();
        s.Name.Should().Be("Solemnity");
        // Structural — no bus to register against.
    }

    // -------- Helpers ----------------------------------------------------

    private static void PlaceOnBattlefield(Enchantment e, Player owner)
    {
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
    }

    private static Creature PlaceCreatureOnBattlefield(
        string name, string cost, int power, int toughness, Player owner)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
