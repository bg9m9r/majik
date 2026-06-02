using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Felidar Savior — Creature — Cat Beast {3}{W}, 2/3.
///
/// Oracle text (Scryfall verified 2026-06):
///   "Lifelink (Damage dealt by this creature also causes you to gain that
///    much life.)
///    When this creature enters, put a +1/+1 counter on each of up to two
///    other target creatures you control."
///
/// Both riders are built on existing engine primitives:
///   * Lifelink (CR 702.15) — KeywordAbility marker.
///   * ETB up-to-two-target +1/+1 counters (CR 603.1 / 603.6a / 115.1) —
///     OnEnterBattlefieldSelf with one 0..2 TargetRequest; one +1/+1 counter
///     placed on each chosen target via CountersService.Add (CR 614).
/// </summary>
[Trait("Color", "W")]
public class FelidarSaviorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Bear(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FelidarSavior_IsCatBeastCreature_AtCost3W_2_3()
    {
        var savior = FelidarSaviorFactory.Create(_alice);

        savior.Name.Should().Be("Felidar Savior");
        savior.HasType(CardType.Creature).Should().BeTrue();
        savior.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        savior.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        savior.ManaCost.Should().Be("{3}{W}");
        savior.Power.Should().Be(2);
        savior.Toughness.Should().Be(3);
        savior.Owner.Should().BeSameAs(_alice);
        savior.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Lifelink — CR 702.15
    // -----------------------------------------------------------------------

    [Fact]
    public void FelidarSavior_HasLifelink()
    {
        var savior = FelidarSaviorFactory.Create(_alice);

        savior.Abilities
            .OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Lifelink");
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape — "up to two other target creatures you control"
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_HasZeroToTwoTargetRequest()
    {
        var savior = FelidarSaviorFactory.Create(_alice);

        var etbTrigger = savior.Abilities
            .OfType<TriggeredAbility>()
            .Single();

        etbTrigger.TargetRequests.Should().ContainSingle();
        etbTrigger.TargetRequests[0].MinTargets.Should().Be(0);
        etbTrigger.TargetRequests[0].MaxTargets.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — one +1/+1 counter on each chosen target
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_PutsOneCounterOnEachOfTwoChosenTargets()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var savior = FelidarSaviorFactory.Create(_alice, triggers);
        savior.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(savior);

        var bearA = Bear(_alice, "Bear A");
        var bearB = Bear(_alice, "Bear B");
        foreach (var b in new[] { bearA, bearB })
        {
            b.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(b);
        }

        bus.Publish(new CardMovedEvent(savior, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "Felidar Savior entering triggers its ETB ability");

        var etbTrigger = savior.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bearA, bearB },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bearA.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bearB.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void EtbTrigger_ZeroTargetsChosen_PlacesNoCounters()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var savior = FelidarSaviorFactory.Create(_alice, triggers);
        savior.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(savior);

        var bear = Bear(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        bus.Publish(new CardMovedEvent(savior, ZoneType.Hand, ZoneType.Battlefield));

        // "up to two" — choosing zero is legal (CR 115.1b).
        var etbTrigger = savior.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new IReadOnlyList<object>[] { Array.Empty<object>() });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "choosing zero targets places no counters");
    }

    [Fact]
    public void EtbTrigger_DoesNotCounterItself_OtherRider()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var savior = FelidarSaviorFactory.Create(_alice, triggers);
        savior.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(savior);

        bus.Publish(new CardMovedEvent(savior, ZoneType.Hand, ZoneType.Battlefield));

        // Even if Felidar Savior is somehow handed to the trigger, the "other"
        // rider (CR 109.5) drops it — it never counters itself.
        var etbTrigger = savior.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { savior },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        savior.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the printed 'other' rider excludes Felidar Savior itself (CR 109.5)");
    }

    [Fact]
    public void EtbTrigger_SkipsTargetNotControlledByOwner()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var savior = FelidarSaviorFactory.Create(_alice, triggers);
        savior.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(savior);

        // A creature Bob controls is not a legal "creature you control"
        // target for Alice — resolution-time legality re-check skips it.
        var bobBear = Bear(_bob);
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        bus.Publish(new CardMovedEvent(savior, ZoneType.Hand, ZoneType.Battlefield));

        var etbTrigger = savior.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobBear },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "a creature you don't control is not a legal target (CR 109.5 / 608.2b)");
    }
}
