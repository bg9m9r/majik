using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TheOzolithFactory"/> — The Ozolith
/// (Ikoria: Lair of Behemoths, Legendary Artifact {1}).
///
/// Oracle text (Scryfall, verified 2026-06-02):
///   "Whenever a creature you control leaves the battlefield, if it had
///    counters on it, put those counters on The Ozolith.
///    At the beginning of combat on your turn, if The Ozolith has counters
///    on it, you may move all counters from The Ozolith onto target
///    creature."
///
/// Covers:
/// - Card identity (name, Legendary Artifact, {1}, owner / controller).
/// - Ability set: two TriggeredAbilities (capture + begin-of-combat move).
/// - Capture trigger fires when a controller's counter-bearing creature
///   leaves the battlefield, copying its counters onto The Ozolith.
/// - Capture trigger ignores opponent creatures and counter-less creatures.
/// - Begin-of-combat move trigger only fires on the controller's own turn
///   while The Ozolith has counters, and moves all counters onto the chosen
///   target creature.
/// </summary>
[Trait("Color", "C")]
public class TheOzolithFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Bear", int p = 2, int t = 2)
    {
        var c = new Creature(name, "1G", p, t) { Owner = owner, Controller = owner };
        return c;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TheOzolith_Identity_LegendaryArtifact_AtCost1()
    {
        var c = TheOzolithFactory.Create(_alice);

        c.Name.Should().Be("The Ozolith");
        c.ManaCost.Should().Be("{1}");
        c.Should().BeOfType<Artifact>();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "The Ozolith is a Legendary Artifact (CR 205.4)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TheOzolith_HasTwoTriggeredAbilities()
    {
        var c = TheOzolithFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "leaves-the-battlefield capture trigger + begin-of-combat move trigger");
    }

    // -----------------------------------------------------------------------
    // Capture trigger — "Whenever a creature you control leaves the
    // battlefield, if it had counters on it, put those counters on The
    // Ozolith."
    // -----------------------------------------------------------------------

    [Fact]
    public void CaptureTrigger_CounterBearingCreatureLeaves_PutsCountersOnOzolith()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ozolith = TheOzolithFactory.Create(_alice, replacements: null, eventBus: bus, triggers: triggers);
        ozolith.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ozolith);
        triggers.BindCard(ozolith);

        // A 2/2 with two +1/+1 counters on Alice's battlefield.
        var bear = MakeCreature(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Counters.Add(CounterType.PlusOnePlusOne, 2);

        // The bear dies (battlefield -> graveyard).
        bus.Publish(new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard));
        triggers.PendingCount.Should().Be(1,
            "the capture trigger queues when a counter-bearing creature you control leaves");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        ozolith.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "CR 122 — the leaving creature's two +1/+1 counters are put on The Ozolith");
    }

    [Fact]
    public void CaptureTrigger_DoesNotFireForCounterlessCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ozolith = TheOzolithFactory.Create(_alice, replacements: null, eventBus: bus, triggers: triggers);
        ozolith.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ozolith);
        triggers.BindCard(ozolith);

        var bear = MakeCreature(_alice); // no counters
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        bus.Publish(new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(0,
            "intervening-if 'if it had counters on it' (CR 603.4) suppresses the trigger");
    }

    [Fact]
    public void CaptureTrigger_DoesNotFireForOpponentCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ozolith = TheOzolithFactory.Create(_alice, replacements: null, eventBus: bus, triggers: triggers);
        ozolith.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ozolith);
        triggers.BindCard(ozolith);

        // Bob's creature with counters leaves — not "a creature YOU control".
        var enemy = MakeCreature(_bob, "Enemy");
        enemy.SetZone(ZoneType.Battlefield);
        enemy.Counters.Add(CounterType.PlusOnePlusOne, 3);

        bus.Publish(new CardMovedEvent(enemy, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(0,
            "the capture trigger is scoped to creatures Alice controls");
        ozolith.Counters.HasAny.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Begin-of-combat move trigger — "At the beginning of combat on your
    // turn, if The Ozolith has counters on it, you may move all counters
    // from The Ozolith onto target creature."
    // -----------------------------------------------------------------------

    [Fact]
    public void MoveTrigger_HasTargetCreatureRequest()
    {
        var c = TheOzolithFactory.Create(_alice);
        var move = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);

        var req = move.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
    }

    [Fact]
    public void MoveTrigger_BeginningOfCombatOnYourTurn_WithCounters_Fires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ozolith = TheOzolithFactory.Create(_alice, replacements: null, eventBus: bus, triggers: triggers);
        ozolith.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ozolith);
        triggers.BindCard(ozolith);
        ozolith.Counters.Add(CounterType.PlusOnePlusOne, 2);

        bus.Publish(new StepStartedEvent(StepStateType.BeginningOfCombat, _alice));

        triggers.PendingCount.Should().Be(1,
            "CR 508.1 begin-combat trigger fires on Alice's turn while The Ozolith has counters");
    }

    [Fact]
    public void MoveTrigger_DoesNotFireOnOpponentTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ozolith = TheOzolithFactory.Create(_alice, replacements: null, eventBus: bus, triggers: triggers);
        ozolith.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ozolith);
        triggers.BindCard(ozolith);
        ozolith.Counters.Add(CounterType.PlusOnePlusOne, 2);

        // Bob's beginning of combat — "on YOUR turn" gates this out.
        bus.Publish(new StepStartedEvent(StepStateType.BeginningOfCombat, _bob));

        triggers.PendingCount.Should().Be(0,
            "'on your turn' restricts the trigger to Alice's own combat");
    }

    [Fact]
    public void MoveTrigger_DoesNotFireWithoutCounters()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ozolith = TheOzolithFactory.Create(_alice, replacements: null, eventBus: bus, triggers: triggers);
        ozolith.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ozolith);
        triggers.BindCard(ozolith);
        // No counters on The Ozolith.

        bus.Publish(new StepStartedEvent(StepStateType.BeginningOfCombat, _alice));

        triggers.PendingCount.Should().Be(0,
            "intervening-if 'if The Ozolith has counters on it' (CR 603.4) suppresses the trigger");
    }

    [Fact]
    public void MoveTrigger_OnResolution_MovesAllCountersOntoTargetCreature()
    {
        var ozolith = TheOzolithFactory.Create(_alice);
        ozolith.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ozolith);
        ozolith.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var target = MakeCreature(_alice, "Recipient");
        target.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(target);

        var move = ozolith.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        move.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        foreach (var e in move.Effects) e.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "CR 122 — all counters move from The Ozolith onto the target creature");
        ozolith.Counters.HasAny.Should().BeFalse(
            "the counters left The Ozolith");
    }

    [Fact]
    public void MoveTrigger_NoChosenTarget_LeavesCountersOnOzolith()
    {
        // "you may" — declining (no chosen target) keeps counters on The
        // Ozolith.
        var ozolith = TheOzolithFactory.Create(_alice);
        ozolith.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ozolith);
        ozolith.Counters.Add(CounterType.PlusOnePlusOne, 2);

        var move = ozolith.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        // No SetChosenTargets call.
        foreach (var e in move.Effects) e.Execute();

        ozolith.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "with no chosen target the may-clause declines and counters stay put");
    }
}
