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
/// - Identity (Instant — Arcane, {1}{W}, owner / controller).
/// - NamedCardFactory dispatch.
/// - Arcane subtype attached so future splice-onto-Arcane riders can splice.
/// - SpellDefinition shape — single 1..1 "target creature" target,
///   Protection intent.
/// - Resolve (shape-only): exiles the targeted creature, no delayed return.
/// - Resolve (full wiring): exiles, returns at end step with +1/+1 counter.
/// - Delayed trigger gates on End step only.
/// </summary>
public class OtherworldlyJourneyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void OtherworldlyJourney_IsInstantArcane_AtCost1W()
    {
        var c = OtherworldlyJourneyFactory.Create(_alice);

        c.Name.Should().Be("Otherworldly Journey");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.HasSubtype(CardSubtype.Arcane).Should().BeTrue(
            "CR 205.3k — Otherworldly Journey is printed Instant — Arcane");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OtherworldlyJourney_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Otherworldly Journey", _alice);

        c.Should().BeOfType<Instant>();
        c.Name.Should().Be("Otherworldly Journey");
        c.HasSubtype(CardSubtype.Arcane).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void OtherworldlyJourney_Definition_HasSingleAnyCreatureTarget()
    {
        var def = OtherworldlyJourneyFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        tr.Intent.Should().Be(BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Resolve — shape-only mode (no TriggerManager)
    // -----------------------------------------------------------------------

    [Fact]
    public void OtherworldlyJourney_Resolve_ShapeOnly_ExilesTarget_NoReturn()
    {
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");

        ResolveCast(_alice, bear, triggers: null);

        bear.Zone.Should().Be(ZoneType.Exile);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Resolve — full wiring (TriggerManager supplied)
    // -----------------------------------------------------------------------

    [Fact]
    public void OtherworldlyJourney_Resolve_ExilesAndReturnsAtEndStep_WithCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");

        ResolveCast(_alice, bear, triggers);

        bear.Zone.Should().Be(ZoneType.Exile);

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bear.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OtherworldlyJourney_Resolve_OpponentCreature_ReturnsToOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bobBear = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        ResolveCast(_alice, bobBear, triggers);

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bobBear.Zone.Should().Be(ZoneType.Battlefield);
        bobBear.Controller.Should().BeSameAs(_bob,
            "CR 614 — return under the OWNER's control");
        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void OtherworldlyJourney_DelayedTrigger_DoesNotFireOnNonEndSteps()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");
        ResolveCast(_alice, bear, triggers);

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(0);
        bear.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ResolveCast(Player caster, ICard target, TriggerManager? triggers)
    {
        var def = OtherworldlyJourneyFactory.BuildSpellDefinition(caster, triggers);
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
