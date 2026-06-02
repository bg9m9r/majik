using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Soulherder (Modern Horizons, {1}{W}{U}).
///
/// Oracle:
///   "Flying."
///   "Whenever a creature is exiled from the battlefield, put a +1/+1
///    counter on Soulherder."
///   "At the beginning of your end step, you may exile target creature
///    you control, then return it to the battlefield under its owner's
///    control."
///
/// Coverage:
///   * Identity (name, type, cost, Spirit subtype, 1/1, owner / controller).
///   * NamedCardFactory dispatch.
///   * Flying keyword (CR 702.9).
///   * Exile trigger: creature moving Battlefield → Exile bumps a +1/+1
///     counter onto Soulherder. Symmetric — works for opponent's creatures.
///   * Exile trigger does NOT fire on graveyard / hand bounces.
///   * End-step trigger flickers a chosen creature you control.
///   * The end-step flicker feeds back into the exile trigger
///     (Soulherder counts its own flicker — CR 603.6 + CR 603.7).
/// </summary>
[Trait("Color", "M")]
public class SoulherderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ------------------------------------------------------------------
    // Identity / dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void Soulherder_IsCreatureSpirit_At1WU_1_1()
    {
        var s = SoulherderFactory.Create(_alice);

        s.Name.Should().Be("Soulherder");
        s.ManaCost.Should().Be("{1}{W}{U}");
        s.HasType(CardType.Creature).Should().BeTrue();
        s.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        s.BasePower.Should().Be(1);
        s.BaseToughness.Should().Be(1);
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Soulherder_HasFlying()
    {
        var s = SoulherderFactory.Create(_alice);

        s.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Flying is the first printed keyword on Soulherder.");
    }

    // ------------------------------------------------------------------
    // Exile trigger — "Whenever a creature is exiled from the
    // battlefield, put a +1/+1 counter on Soulherder."
    // ------------------------------------------------------------------

    [Fact]
    public void Soulherder_CreatureBattlefieldToExile_AddsPlusOneCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var soulherder = SoulherderFactory.Create(_alice, zones, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(soulherder);
        soulherder.SetZone(ZoneType.Battlefield);

        // An opponent's creature gets exiled from the battlefield — printed
        // text has no controller filter, so Soulherder's counter trigger
        // fires regardless of whose creature was exiled (CR 603.6).
        var victim = new Creature("Bob's Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        victim.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(victim);

        zones.MoveCard(victim, ZoneType.Battlefield, ZoneType.Exile, _bob);

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        soulherder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "exiling a creature from the battlefield triggers the +1/+1 " +
            "counter rider on Soulherder.");
    }

    [Fact]
    public void Soulherder_NonCreatureExile_DoesNotTrigger()
    {
        // CR 603.6 — the trigger is keyed on the exiled card being a
        // CREATURE. An artifact / enchantment going to exile shouldn't
        // bump the counter.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var soulherder = SoulherderFactory.Create(_alice, zones, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(soulherder);
        soulherder.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        zones.MoveCard(artifact, ZoneType.Battlefield, ZoneType.Exile, _bob);

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        soulherder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Soulherder's exile trigger is gated on the exiled card being " +
            "a creature; an exiled artifact does NOT bump the counter.");
    }

    [Fact]
    public void Soulherder_CreatureToGraveyard_DoesNotTrigger()
    {
        // "Exiled from the battlefield" — Battlefield → Graveyard isn't
        // an exile move, so the trigger must not fire on a die-to-grave
        // event (CR 603.6).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var soulherder = SoulherderFactory.Create(_alice, zones, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(soulherder);
        soulherder.SetZone(ZoneType.Battlefield);

        var victim = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        victim.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(victim);

        zones.MoveCard(victim, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        soulherder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Battlefield → Graveyard is not an exile move; the trigger " +
            "only fires on the exile destination.");
    }

    // ------------------------------------------------------------------
    // End-step flicker trigger — "At the beginning of your end step, you
    // may exile target creature you control, then return it to the
    // battlefield under its owner's control."
    // ------------------------------------------------------------------

    [Fact]
    public void Soulherder_EndStep_FlickersTargetCreatureYouControl()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var soulherder = SoulherderFactory.Create(_alice, zones, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(soulherder);
        soulherder.SetZone(ZoneType.Battlefield);

        // A creature Alice controls that we'll target with the flicker.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        // Pre-supply the flicker target via SetChosenTargets — same shape
        // as Spell Queller / Sword of Hearth and Home tests.
        var flickerTrigger = soulherder.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice)));
        flickerTrigger.SetChosenTargets(new[] { new[] { (object)bear } });

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        // Drain triggers in a loop — the flicker resolution itself
        // publishes CardMovedEvent which queues another (the exile
        // trigger). Re-pump pending triggers onto the stack until the
        // queue settles. CR 603.6 + CR 603.7 — newly-queued triggers
        // observe the resolving environment and stack independently.
        for (var iteration = 0; iteration < 4; iteration++)
        {
            triggers.PutPendingTriggersOnStack(_alice);
            while (stack.Count > 0) stack.Pop()!.Resolve();
        }

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "the flicker exiles then returns the target creature in the " +
            "same resolution (CR 701.20).");
        bear.Controller.Should().BeSameAs(_alice,
            "the card returns under its owner's control (CR 110.2).");

        // The flicker fed back through Soulherder's own exile trigger —
        // exiling the bear during the flicker bumps Soulherder's counter.
        // Publishing StepStartedEvent fired the flicker, the bear went
        // Battlefield → Exile (counter +1), then Exile → Battlefield.
        soulherder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Soulherder's own exile trigger fires on its own end-step " +
            "flicker — CR 603.6 + CR 603.7 (independent triggers stack " +
            "even when one feeds the other).");
    }

    [Fact]
    public void Soulherder_EndStep_NoTarget_NoOps()
    {
        // "You may" + no target supplied = the resolve body short-circuits.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var soulherder = SoulherderFactory.Create(_alice, zones, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(soulherder);
        soulherder.SetZone(ZoneType.Battlefield);

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        var act = () => { while (stack.Count > 0) stack.Pop()!.Resolve(); };
        act.Should().NotThrow();

        soulherder.Zone.Should().Be(ZoneType.Battlefield,
            "no target — flicker no-ops; Soulherder stays in play.");
        soulherder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no flicker means no exile event means no counter bump.");
    }

    [Fact]
    public void Soulherder_EndStep_OpponentsTurn_DoesNotFire()
    {
        // OnStepBegin gates on the controller's own End step — Bob's End
        // step should not trigger Alice's Soulherder.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var soulherder = SoulherderFactory.Create(_alice, zones, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(soulherder);
        soulherder.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Bob's end step doesn't fire 'at the beginning of YOUR end " +
            "step' — bear should never have flickered.");
        soulherder.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }
}
