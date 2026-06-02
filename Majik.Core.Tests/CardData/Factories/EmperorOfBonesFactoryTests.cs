using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="EmperorOfBonesFactory"/>.
///
/// Card: Emperor of Bones (Modern Horizons 3, {1}{B}). Creature —
/// Skeleton Noble. 2/2.
///
/// Coverage:
/// <list type="bullet">
///   <item>Identity ({1}{B}, 2/2, Creature, Skeleton, Noble).</item>
///   <item>Dispatch via <see cref="NamedCardFactory"/>.</item>
///   <item>Ability shape — 1 begin-of-combat triggered, 1 activated
///       Adapt, 1 counter-added triggered (plus the Adapt keyword
///       marker stamped by <see cref="AdaptFactory"/>).</item>
///   <item>Ability 1 — exile-target-card-from-graveyard tracks the
///       exile in the per-Emperor state ledger; fires only on the
///       controller's own begin-of-combat step.</item>
///   <item>Ability 2 — Adapt 2 places 2 +1/+1 counters when none
///       present, no-op when already present.</item>
///   <item>Ability 3 — +1/+1-counter trigger picks a creature from
///       the ledger, puts it onto the battlefield under the Emperor's
///       controller with a finality counter + haste. With an empty
///       ledger, the trigger is a clean no-op.</item>
///   <item>End-to-end — Adapt 2 → ability 3 fires → exile-tracked
///       creature returns with finality + haste; subsequent sac at
///       end step redirects to exile (CR 122.1m).</item>
/// </list>
/// </summary>
[Trait("Color", "B")]
public class EmperorOfBonesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private record EmperorRig(
        Creature Emperor,
        TriggerManager Triggers,
        Majik.Core.Stack.Stack Stack,
        ZoneService Zones,
        ReplacementBus Reps,
        EventBus Bus);

    private EmperorRig MakeEmperor()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var reps = new ReplacementBus();
        var zones = new ZoneService(bus, reps);
        var triggers = new TriggerManager(stack, bus);
        var emperor = EmperorOfBonesFactory.Create(
            _alice, triggers, zones, reps, bus, agent: null);
        emperor.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(emperor);
        triggers.BindCard(emperor);
        return new EmperorRig(emperor, triggers, stack, zones, reps, bus);
    }

    [Fact]
    public void EmperorOfBones_Identity()
    {
        var emperor = EmperorOfBonesFactory.Create(_alice);

        emperor.Name.Should().Be("Emperor of Bones");
        emperor.ManaCost.Should().Be("{1}{B}");
        emperor.BasePower.Should().Be(2);
        emperor.BaseToughness.Should().Be(2);
        emperor.HasType(CardType.Creature).Should().BeTrue();
        emperor.Subtypes.Should().Contain(CardSubtype.Skeleton);
        emperor.Subtypes.Should().Contain(CardSubtype.Noble);
        emperor.Owner.Should().BeSameAs(_alice);
        emperor.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void EmperorOfBones_AbilityShape()
    {
        var emperor = EmperorOfBonesFactory.Create(_alice);

        // One activated ability (Adapt 2).
        emperor.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);

        // Two triggered abilities: ability 1 (begin-of-combat) and
        // ability 3 (counter-added).
        emperor.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);

        // Adapt keyword marker stamped by AdaptFactory.
        emperor.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Adapt 2");
    }

    [Fact]
    public void Ability1_BeginCombatTrigger_ExilesGraveyardCard_AndTracksIt()
    {
        var rig = MakeEmperor();

        // Pre-seed Alice's graveyard with a creature card (v1 auto-pick
        // looks at controller's graveyard first).
        var zombie = new Creature("Test Zombie", "{B}", 2, 2)
        { Owner = _alice, Controller = _alice };
        zombie.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(zombie);

        // Fire the begin-of-combat step on Alice's turn.
        var step = new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice);
        rig.Bus.Publish(step);

        rig.Triggers.PendingCount.Should().BeGreaterThan(0,
            "begin-of-combat trigger should queue on Alice's combat step");

        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        zombie.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(zombie);

        var state = EmperorOfBonesFactory.GetState(rig.Emperor);
        state.Should().NotBeNull();
        state!.ExiledWith.Should().Contain(zombie);
    }

    [Fact]
    public void Ability1_BeginCombatTrigger_DoesNotFire_OnOpponentsTurn()
    {
        var rig = MakeEmperor();

        var zombie = new Creature("Test Zombie", "{B}", 2, 2)
        { Owner = _alice, Controller = _alice };
        zombie.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(zombie);

        // Bob's begin-of-combat — Emperor should NOT trigger.
        var step = new StepStartedEvent(PhaseStateType.BeginningOfCombat, _bob);
        rig.Bus.Publish(step);
        rig.Triggers.PutPendingTriggersOnStack(_bob);

        rig.Stack.IsEmpty.Should().BeTrue();
        zombie.Zone.Should().Be(ZoneType.Graveyard);
        EmperorOfBonesFactory.GetState(rig.Emperor)!.ExiledWith
            .Should().BeEmpty();
    }

    [Fact]
    public void Ability2_AdaptTwo_PlacesTwoCounters_WhenNonePresent()
    {
        var emperor = EmperorOfBonesFactory.Create(_alice);
        emperor.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(emperor);

        var adapt = emperor.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();

        emperor.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void Ability2_AdaptTwo_IsNoOp_WhenPlusOneCountersAlreadyPresent()
    {
        var emperor = EmperorOfBonesFactory.Create(_alice);
        emperor.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(emperor);
        emperor.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var adapt = emperor.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();

        emperor.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(1, because: "CR 702.116b — Adapt fizzles when counters already present");
    }

    [Fact]
    public void Ability3_CounterTrigger_WithEmptyExileLedger_IsNoOp()
    {
        var rig = MakeEmperor();

        // Trigger ability 3 by adding +1/+1 counters via CountersService
        // (the surface Adapt routes through).
        CountersService.Add(rig.Emperor, CounterType.PlusOnePlusOne, 1, rig.Reps, rig.Bus);

        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        // Empty ledger → no return; only Emperor is on Alice's battlefield.
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(rig.Emperor);
    }

    [Fact]
    public void Ability3_CounterTrigger_ReturnsExiledCreature_WithFinalityCounter_AndHaste()
    {
        var rig = MakeEmperor();

        // Seed a creature into Alice's exile + the Emperor's ledger.
        var zombie = new Creature("Test Zombie", "{B}", 2, 2)
        { Owner = _alice, Controller = _alice };
        zombie.SetZone(ZoneType.Exile);
        _alice.Zones.Exile.AddCard(zombie);
        EmperorOfBonesFactory.GetState(rig.Emperor)!.AddExiledWith(zombie);

        // Adapt 2 — fires CountersService.Add → CounterAddedEvent →
        // ability 3 trigger.
        var adapt = rig.Emperor.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();

        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        zombie.Zone.Should().Be(ZoneType.Battlefield);
        zombie.Controller.Should().BeSameAs(_alice);
        zombie.Counters.Count(CounterType.Finality)
            .Should().Be(1, because: "the return rider says 'with a finality counter on it'");
        zombie.HasSummoningSickness.Should().BeFalse(because: "haste");

        // Ledger consumed.
        EmperorOfBonesFactory.GetState(rig.Emperor)!.ExiledWith
            .Should().NotContain(zombie);
    }

    [Fact]
    public void EndToEnd_FinalityRedirectsDelayedSacToExile_NotGraveyard()
    {
        var rig = MakeEmperor();

        var zombie = new Creature("Test Zombie", "{B}", 2, 2)
        { Owner = _alice, Controller = _alice };
        zombie.SetZone(ZoneType.Exile);
        _alice.Zones.Exile.AddCard(zombie);
        EmperorOfBonesFactory.GetState(rig.Emperor)!.AddExiledWith(zombie);

        // Adapt 2 → ability 3 → zombie returns with finality counter.
        var adapt = rig.Emperor.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in adapt.Effects) eff.Execute();
        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        zombie.Zone.Should().Be(ZoneType.Battlefield);
        zombie.Counters.Count(CounterType.Finality).Should().Be(1);

        // Ensure StepStartedEvent.Timestamp strictly post-dates the
        // delayed-trigger resolvedAt fence (DelayedTriggeredAbility uses
        // an event-time gate).
        System.Threading.Thread.Sleep(5);

        var endStep = new StepStartedEvent(PhaseStateType.End, _alice);
        rig.Bus.Publish(endStep);
        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        zombie.Zone.Should().Be(ZoneType.Exile,
            because: "finality counter redirects sac → exile (CR 122.1m)");
        _alice.Zones.Exile.GetCards().Should().Contain(zombie);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(zombie);
    }
}
