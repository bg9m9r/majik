using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Cackling Prowler (Tarkir: Dragonstorm) — Creature —
/// Hyena Rogue {3}{G} 4/3.
///   "Ward {2} (Whenever this creature becomes the target of a spell or ability
///    an opponent controls, counter it unless that player pays {2}.)
///    Morbid — At the beginning of your end step, if a creature died this turn,
///    put a +1/+1 counter on this creature."
///
/// Covers (card-unique behaviour only — CardFactoryContractTests already asserts
/// dispatch + well-formedness for every implemented card):
///   - Identity (mana cost / P-T / subtypes).
///   - Ward keyword marker (CR 702.21).
///   - CR 603.4 Morbid end-step intervening-if: with a creature having died this
///     turn the "your end step" trigger puts a +1/+1 counter on the Prowler;
///     with no creature death it no-ops; and it only fires on the controller's
///     end step.
/// </summary>
[Trait("Color", "G")]
public class CacklingProwlerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ------------------------------------------------------------------
    // Card identity
    // ------------------------------------------------------------------

    [Fact]
    public void CacklingProwler_Identity()
    {
        var c = CacklingProwlerFactory.Create(_alice);

        c.Name.Should().Be("Cackling Prowler");
        c.ManaCost.Should().Be("{3}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Hyena).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CacklingProwler_HasWardMarker()
    {
        var c = CacklingProwlerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Ward", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Cackling Prowler has Ward {2} (CR 702.21)");
    }

    // ------------------------------------------------------------------
    // CR 603.4 — Morbid end-step "a creature died this turn" counter trigger
    // ------------------------------------------------------------------

    [Fact]
    public void CacklingProwler_EndStep_AfterCreatureDied_PutsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turnState = new TurnState();

        var prowler = CacklingProwlerFactory.Create(
            _alice, bus, triggers, replacements: null, turnStateResolver: () => turnState);
        _alice.Zones.Battlefield.AddCard(prowler);
        prowler.SetZone(ZoneType.Battlefield);

        // A creature died this turn — CR 700.4 counts ANY creature regardless of
        // controller, so record one under the opponent to prove the gate is
        // global, not controller-scoped.
        turnState.RecordCreatureDied(_bob);
        turnState.CreaturesDiedThisTurn.Should().Be(1);

        // Alice's (the controller's) end step fires.
        bus.Publish(new StepStartedEvent(StepStateType.End, _alice));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "the Morbid end-step counter trigger fires on the controller's end step");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);

        prowler.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a +1/+1 counter is put on the Prowler (CR 121.1)");
    }

    [Fact]
    public void CacklingProwler_EndStep_NoCreatureDied_DoesNotPutCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turnState = new TurnState();

        var prowler = CacklingProwlerFactory.Create(
            _alice, bus, triggers, replacements: null, turnStateResolver: () => turnState);
        _alice.Zones.Battlefield.AddCard(prowler);
        prowler.SetZone(ZoneType.Battlefield);

        // No creature died → Morbid intervening-if (CR 603.4) fails.
        turnState.CreaturesDiedThisTurn.Should().Be(0);

        bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);

        prowler.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no creature died this turn → no counter (CR 603.4)");
    }

    [Fact]
    public void CacklingProwler_EndStep_OnlyFiresOnControllersEndStep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turnState = new TurnState();

        var prowler = CacklingProwlerFactory.Create(
            _alice, bus, triggers, replacements: null, turnStateResolver: () => turnState);
        _alice.Zones.Battlefield.AddCard(prowler);
        prowler.SetZone(ZoneType.Battlefield);

        turnState.RecordCreatureDied(_bob);

        // The End step belongs to BOB, not the Prowler's controller — "your end
        // step" must NOT fire (CR 500).
        bus.Publish(new StepStartedEvent(StepStateType.End, _bob));

        triggers.PendingCount.Should().Be(0,
            "\"your end step\" fires only on the controller's end step");
    }

    [Fact]
    public void CacklingProwler_NullTurnStateResolver_NoCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // No turn-state resolver (shape path) → Morbid gate false even though the
        // trigger fires.
        var prowler = CacklingProwlerFactory.Create(
            _alice, bus, triggers, replacements: null, turnStateResolver: null);
        _alice.Zones.Battlefield.AddCard(prowler);
        prowler.SetZone(ZoneType.Battlefield);

        bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);

        prowler.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no TurnState wired → Morbid gate false → no counter");
    }
}
