using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="KalitasTraitorOfGhetFactory"/> — Legendary
/// Creature {2}{B}{B} (Oath of the Gatewatch).
///
/// Oracle (v1 — triggered-after-death approximation):
///   "Lifelink.
///    If a nontoken creature an opponent controls would die, exile it
///    instead and you create a 2/2 black Zombie creature token."
///
/// Covers:
/// - Identity (Legendary Vampire Knight 3/4 at {2}{B}{B}).
/// - NamedCardFactory dispatch.
/// - Lifelink marker.
/// - Trigger fires on opponent's nontoken creature dying; rejects own
///   creatures + token creatures + non-creature deaths.
/// - Resolve exiles the dying creature and spawns a 2/2 black Zombie
///   token under Kalitas's controller.
/// </summary>
public class KalitasTraitorOfGhetTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(string name, Player owner, bool isToken = false)
    {
        var c = new Creature(name, manaCost: "1G", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        if (isToken) c.MarkAsToken();
        return c;
    }

    private static void PlaceKalitasOnBattlefield(Player controller, Creature kalitas)
    {
        // Kalitas's dies trigger has ActiveZones = {Battlefield, Graveyard};
        // the trigger's IsTriggered gate (TriggeredAbility.IsTriggered)
        // checks the source's current zone, so predicate-only tests still
        // need Kalitas to live in one of the active zones to evaluate.
        controller.Zones.Battlefield.AddCard(kalitas);
        kalitas.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Kalitas_Identity_LegendaryVampireKnight_3_4_AtCost2BB()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);

        k.Name.Should().Be("Kalitas, Traitor of Ghet");
        k.ManaCost.Should().Be("{2}{B}{B}");
        k.HasType(CardType.Creature).Should().BeTrue();
        k.Supertypes.Should().Contain(CardSupertype.Legendary);
        k.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        k.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        k.BasePower.Should().Be(3);
        k.BaseToughness.Should().Be(4);
        k.Owner.Should().BeSameAs(_alice);
        k.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Kalitas_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kalitas, Traitor of Ghet", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Kalitas, Traitor of Ghet");
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
    }

    [Fact]
    public void Kalitas_HasLifelinkMarker()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);

        k.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Lifelink",
                "Kalitas's printed text leads with Lifelink (CR 702.15)");
    }

    [Fact]
    public void Kalitas_HasSingleDiesTrigger()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);
        k.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Trigger predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void Kalitas_OpponentsNontokenCreatureDies_FiresTrigger()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);
        PlaceKalitasOnBattlefield(_alice, k);
        var trigger = k.Abilities.OfType<TriggeredAbility>().Single();

        var bobsBear = MakeCreature("Bear", _bob);
        var moved = new CardMovedEvent(bobsBear, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.IsTriggered(moved).Should().BeTrue();
    }

    [Fact]
    public void Kalitas_OwnCreatureDying_DoesNotFire()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);
        PlaceKalitasOnBattlefield(_alice, k);
        var trigger = k.Abilities.OfType<TriggeredAbility>().Single();

        var alicesBear = MakeCreature("Own Bear", _alice);
        var moved = new CardMovedEvent(alicesBear, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.IsTriggered(moved).Should().BeFalse(
            "printed text gates on 'an opponent controls'");
    }

    [Fact]
    public void Kalitas_OpponentsTokenCreatureDying_DoesNotFire()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);
        PlaceKalitasOnBattlefield(_alice, k);
        var trigger = k.Abilities.OfType<TriggeredAbility>().Single();

        var bobsToken = MakeCreature("Saproling", _bob, isToken: true);
        var moved = new CardMovedEvent(bobsToken, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.IsTriggered(moved).Should().BeFalse(
            "printed text gates on 'nontoken creature' (CR 111.3)");
    }

    [Fact]
    public void Kalitas_NonCreatureDying_DoesNotFire()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);
        PlaceKalitasOnBattlefield(_alice, k);
        var trigger = k.Abilities.OfType<TriggeredAbility>().Single();

        var bobsRing = new Artifact("Sol Ring", "1");
        bobsRing.SetOwner(_bob);
        bobsRing.SetController(_bob);
        var moved = new CardMovedEvent(bobsRing, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.IsTriggered(moved).Should().BeFalse(
            "trigger is gated on Creature card type");
    }

    // -----------------------------------------------------------------------
    // Trigger resolution — exile + token spawn
    // -----------------------------------------------------------------------

    [Fact]
    public void Kalitas_Resolution_ExilesDyingCreature_AndSpawnsBlackZombie()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var k = KalitasTraitorOfGhetFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(k);
        k.SetZone(ZoneType.Battlefield);

        // Bob's nontoken creature dies (move to graveyard before the event).
        var bobsBear = MakeCreature("Bear", _bob);
        _bob.Zones.Battlefield.AddCard(bobsBear);
        bobsBear.SetZone(ZoneType.Battlefield);
        // Route the death through ZoneService so the trigger sees the
        // standard Battlefield → Graveyard CardMovedEvent (Wurmcoil-posture
        // — ZoneService stamps the graveyard zone before publishing).
        zones.MoveCardTo(bobsBear, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Bear should be in Bob's exile (CR 110 — exile is a public zone
        // shared across the game; the dying card's owner owns it).
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobsBear);
        _bob.Zones.Exile.GetCards().Should().Contain(bobsBear);
        bobsBear.Zone.Should().Be(ZoneType.Exile);

        // A 2/2 black Zombie token should be on Alice's battlefield.
        var zombies = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Zombie")
            .ToList();
        zombies.Should().HaveCount(1);
        zombies[0].BasePower.Should().Be(2);
        zombies[0].BaseToughness.Should().Be(2);
        zombies[0].HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        zombies[0].Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Pump activated ability — "{2}{B}, Sacrifice another Vampire or Zombie:
    // Put two +1/+1 counters on Kalitas." This is the missing-effect the
    // Layer-B audit surfaced (previously only Lifelink + the dies-trigger
    // were bound).
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_Exists_WithManaAndSacrificeCost()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);

        var pump = k.Abilities.OfType<ActivatedAbility>().Single();
        pump.Costs.OfType<Majik.Core.Costs.ManaCostCost>().Should().ContainSingle(
            "the pump costs {2}{B}");
        pump.Costs.OfType<Majik.Core.Costs.SacrificeFilteredCost>().Should().ContainSingle(
            "the pump sacrifices another Vampire or Zombie");
    }

    [Fact]
    public void PumpAbility_SacrificesVampireOrZombie_AndAddsTwoCounters()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);
        PlaceKalitasOnBattlefield(_alice, k);

        // A Zombie the controller controls, eligible to sacrifice.
        var zombie = new Creature("Walking Corpse", "{1}{B}", 2, 2,
            subtypes: new[] { CardSubtype.Zombie });
        zombie.SetOwner(_alice);
        zombie.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(zombie);
        zombie.SetZone(ZoneType.Battlefield);

        var pump = k.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = pump.Costs.OfType<Majik.Core.Costs.SacrificeFilteredCost>().Single();

        // Kalitas itself is NOT eligible ("another"); the Zombie is.
        sacCost.CanPay(_alice).Should().BeTrue("the controller has another Zombie to sacrifice");
        sacCost.Pay(_alice);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(zombie,
            "the Zombie was sacrificed");
        _alice.Zones.Battlefield.GetCards().Should().Contain(k,
            "Kalitas itself is not sacrificed ('another')");

        foreach (var effect in pump.Effects) effect.Execute();
        k.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne).Should().Be(2,
            "the pump puts two +1/+1 counters on Kalitas");
    }

    [Fact]
    public void PumpAbility_CannotPay_WithNoOtherVampireOrZombie()
    {
        var k = KalitasTraitorOfGhetFactory.Create(_alice);
        PlaceKalitasOnBattlefield(_alice, k);

        var pump = k.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = pump.Costs.OfType<Majik.Core.Costs.SacrificeFilteredCost>().Single();

        // Only Kalitas (a Vampire) is on the battlefield — but it can't
        // sacrifice ITSELF ("another"), so the cost is unpayable.
        sacCost.CanPay(_alice).Should().BeFalse(
            "Kalitas can't sacrifice itself; no OTHER Vampire/Zombie present");
    }
}
