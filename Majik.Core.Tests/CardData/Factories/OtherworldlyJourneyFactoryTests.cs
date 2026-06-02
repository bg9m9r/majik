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
/// Unit tests for <see cref="OtherworldlyJourneyFactory"/>.
///
/// Covers:
/// - Identity (Instant, {1}{W}, owner / controller).
/// - NamedCardFactory dispatch.
/// - SpellDefinition shape — single 1..1 "target creature", Protection
///   intent.
/// - Resolve (shape-only, no TriggerManager): exiles the targeted creature
///   but skips the delayed return (CR 701.21 only — same posture as Touch
///   the Spirit Realm shape-only mode).
/// - Resolve + end-step delayed trigger: returns the exiled creature to
///   the battlefield under its owner's control with a +1/+1 counter
///   (CR 603.7 + CR 614 + CR 122.1c).
/// - Resolve: illegal target (already left the battlefield) fizzles
///   (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class OtherworldlyJourneyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void OtherworldlyJourney_IsInstant_AtCost1W()
    {
        var c = OtherworldlyJourneyFactory.Create(_alice);

        c.Name.Should().Be("Otherworldly Journey");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void OtherworldlyJourney_Definition_HasSingleCreatureTarget()
    {
        var def = OtherworldlyJourneyFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        // Note: no "you control" gate — opponents' creatures are legal targets.
        tr.Intent.Should().Be(BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Resolve — shape-only (no TriggerManager → no delayed return)
    // -----------------------------------------------------------------------

    [Fact]
    public void OtherworldlyJourney_Resolve_ShapeOnly_ExilesButSkipsDelayedReturn()
    {
        // No TriggerManager supplied → exile happens, delayed return is
        // skipped (matches Touch the Spirit Realm / Yorion shape-only posture).
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");

        var def = OtherworldlyJourneyFactory.BuildSpellDefinition(_alice);
        ExecuteCast(def, bear);

        bear.Zone.Should().Be(ZoneType.Exile,
            "CR 701.21 — exile fires; shape-only mode skips the delayed return");
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "shape-only mode never reaches the +1/+1 counter placement");
    }

    [Fact]
    public void OtherworldlyJourney_Resolve_IllegalTarget_Fizzles()
    {
        // Creature that left the battlefield before the spell resolves —
        // the resolve-time legality check (CR 608.2b) should short-circuit.
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");
        // Move bear off battlefield to simulate prior removal in response.
        _alice.Zones.Battlefield.RemoveCard(bear);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var def = OtherworldlyJourneyFactory.BuildSpellDefinition(_alice);
        ExecuteCast(def, bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "target no longer on the battlefield → CR 608.2b no-effect");
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Resolve + delayed end-step return + counter
    // -----------------------------------------------------------------------

    [Fact]
    public void OtherworldlyJourney_Resolve_ExilesThenReturnsAtEndStepWithCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bear = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");

        var def = OtherworldlyJourneyFactory.BuildSpellDefinition(
            _alice, triggers: triggers, zones: null, replacements: null);
        ExecuteCast(def, bear);

        bear.Zone.Should().Be(ZoneType.Exile, "CR 701.21 exile fires immediately");

        // Fire the delayed end-step return.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(1,
            "the delayed end-step return trigger is pending after the End step starts");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "CR 603.7 + CR 614 — delayed end-step trigger returns the exiled card");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        bear.Controller.Should().BeSameAs(_alice,
            "return is 'under its owner's control'");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 122.1c — return places a +1/+1 counter on the returned card");
    }

    [Fact]
    public void OtherworldlyJourney_Resolve_TargetingOpponentsCreature_ExilesAndReturnsToOpponent()
    {
        // "target creature" — any controller. Cast on Bob's creature.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bobBear = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        var def = OtherworldlyJourneyFactory.BuildSpellDefinition(
            _alice, triggers: triggers, zones: null, replacements: null);
        ExecuteCast(def, bobBear);

        bobBear.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(bobBear,
            "card routes to its owner's exile pile, not the caster's");

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bobBear.Zone.Should().Be(ZoneType.Battlefield);
        bobBear.Controller.Should().BeSameAs(_bob,
            "CR 614 — return is 'under its owner's control', not the caster's");
        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ExecuteCast(SpellDefinition def, Creature target)
    {
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var bear = new Creature(name, cost, 2, 2);
        bear.SetOwner(owner);
        bear.SetController(owner);
        owner.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }
}
