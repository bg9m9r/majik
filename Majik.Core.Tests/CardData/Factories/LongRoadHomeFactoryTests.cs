using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LongRoadHomeFactory"/>.
///
/// Covers:
/// - Identity (Instant, {1}{W}, owner / controller).
/// - NamedCardFactory dispatch.
/// - SpellDefinition shape — single 1..1 "target creature" target,
///   Protection intent. CandidateGatherer walks all players' battlefields.
/// - Resolve (shape-only): exiles the targeted creature, no delayed return
///   when no <see cref="TriggerManager"/> is supplied.
/// - Resolve (full wiring): exiles the targeted creature; on end-step
///   delayed trigger, returns it under owner's control with a +1/+1
///   counter (CR 614).
/// - Resolve: opponent's creature still gets exiled+returned (no "you
///   control" filter — Long Road Home reads "target creature").
/// - Resolve: target that left the battlefield before resolution → no-op
///   (CR 608.2b).
/// - Delayed trigger fires only on End step (not Upkeep / Draw / etc.).
/// </summary>
[Trait("Color", "W")]
public class LongRoadHomeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LongRoadHome_IsInstant_AtCost1W()
    {
        var c = LongRoadHomeFactory.Create(_alice);

        c.Name.Should().Be("Long Road Home");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void LongRoadHome_Definition_HasSingleAnyCreatureTarget()
    {
        var def = LongRoadHomeFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        tr.Description.Should().NotContain("you control",
            "Long Road Home targets ANY creature, not just controller-side");
        tr.Intent.Should().Be(BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Resolve — shape-only mode (no TriggerManager)
    // -----------------------------------------------------------------------

    [Fact]
    public void LongRoadHome_Resolve_ShapeOnly_ExilesTarget_NoReturn()
    {
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");

        ResolveCast(_alice, bear, triggers: null);

        bear.Zone.Should().Be(ZoneType.Exile,
            "shape-only mode still runs the exile half");
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the +1/+1 counter is placed only on the delayed return — no return without a TriggerManager");
    }

    // -----------------------------------------------------------------------
    // Resolve — full wiring (TriggerManager supplied)
    // -----------------------------------------------------------------------

    [Fact]
    public void LongRoadHome_Resolve_ExilesAndReturnsAtEndStep_WithPlusOnePlusOneCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");

        ResolveCast(_alice, bear, triggers);

        bear.Zone.Should().Be(ZoneType.Exile, "exile half runs immediately (CR 701.21)");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the +1/+1 counter is placed only on the delayed return");

        // Publish an End-step started event — the delayed return should fire.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(1, "the delayed end-step return is pending");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "delayed end-step trigger returns the exiled creature (CR 603.7 + CR 614)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        bear.Controller.Should().BeSameAs(_alice,
            "CR 614 — return is under the owner's control (owner = Alice here)");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 614 — the +1/+1 counter is placed as the card re-enters");
    }

    [Fact]
    public void LongRoadHome_Resolve_OpponentCreature_StillExilesAndReturnsUnderOpponentOwner()
    {
        // Long Road Home has no "you control" filter — Alice can blink
        // Bob's creature. The return is "under its OWNER's control" so
        // Bob's bear comes back to Bob, even though Alice cast the spell.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bobBear = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        ResolveCast(_alice, bobBear, triggers);

        bobBear.Zone.Should().Be(ZoneType.Exile,
            "Long Road Home exiles any creature, not just controller-side ones");
        _bob.Zones.Exile.GetCards().Should().Contain(bobBear);

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bobBear.Zone.Should().Be(ZoneType.Battlefield);
        bobBear.Controller.Should().BeSameAs(_bob,
            "CR 614 — return under the owner's control (Bob is the owner)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);
        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void LongRoadHome_Resolve_TargetLeftBattlefieldBeforeResolution_NoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");
        // Simulate a removal spell killing the bear before Long Road Home
        // resolves — CR 608.2b illegal target → no effect.
        _alice.Zones.Battlefield.RemoveCard(bear);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        ResolveCast(_alice, bear, triggers);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — illegal target (left the battlefield) → no effect");
        triggers.PendingCount.Should().Be(0,
            "no delayed return registered when the cast fizzles");
    }

    [Fact]
    public void LongRoadHome_DelayedTrigger_DoesNotFireOnNonEndSteps()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");
        ResolveCast(_alice, bear, triggers);

        // Pump a few non-End steps — the delayed return must not fire.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        triggers.PendingCount.Should().Be(0,
            "delayed end-step trigger gates on StepType == End (CR 603.7)");

        bear.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ResolveCast(Player caster, ICard target, TriggerManager? triggers)
    {
        var def = LongRoadHomeFactory.BuildSpellDefinition(caster, triggers);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
