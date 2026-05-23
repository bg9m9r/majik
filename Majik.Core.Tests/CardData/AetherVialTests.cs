using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AetherVialFactory"/> (Darksteel, {1}).
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Upkeep trigger structure + behaviour: adds a Charge counter (v1
///   auto-accept).
/// - Tap activated ability shape: cost = tap-self only.
/// - Activate with 2 counters: a creature card with mv 2 is moved from
///   hand to battlefield; mv-mismatched creatures stay in hand.
/// - Activate with 0 counters: only mv-0 creatures are eligible.
/// - Activate with no matching creature in hand: no-op.
/// - ETB triggers fire on the placed creature when routed through
///   ZoneService (CR 603.6a regression).
/// </summary>
public class AetherVialTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherVial_Identity()
    {
        var vial = AetherVialFactory.Create(_alice);

        vial.Name.Should().Be("Aether Vial");
        vial.ManaCost.Should().Be("{1}");
        vial.HasType(CardType.Artifact).Should().BeTrue();
        vial.Owner.Should().BeSameAs(_alice);
        vial.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AetherVial_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Aether Vial", _alice);

        card.Should().BeOfType<Artifact>("Aether Vial is an Artifact");
        card.Name.Should().Be("Aether Vial");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Upkeep trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherVial_UpkeepTrigger_AddsChargeCounter()
    {
        var vial = AetherVialFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vial);
        vial.SetZone(ZoneType.Battlefield);

        vial.Counters.Count(CounterType.Charge).Should().Be(0);

        var trigger = vial.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        vial.Counters.Count(CounterType.Charge).Should().Be(1,
            "upkeep auto-accept adds a charge counter at v1");
    }

    [Fact]
    public void AetherVial_UpkeepTrigger_OnlyFiresOnControllersOwnUpkeep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var vial = AetherVialFactory.Create(
            _alice, zoneService: null, eventBus: bus, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(vial);
        vial.SetZone(ZoneType.Battlefield);

        // Bob's upkeep — should NOT trigger.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "Aether Vial only triggers on its controller's own upkeep");

        // Alice's upkeep — surfaces as pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Tap activated ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherVial_ActivatedAbility_HasTapSelfCost_NoManaCost()
    {
        var vial = AetherVialFactory.Create(_alice);

        var ability = vial.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().ContainSingle(
            "Aether Vial's only cost is {T}");
        ability.Costs.OfType<AdditionalCost>().Single().CostType
            .Should().Be(AdditionalCostType.Tap);
        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Aether Vial's activation has no mana cost");
    }

    // -----------------------------------------------------------------------
    // Tap activated ability — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherVial_Activate_WithTwoCounters_MovesManaValueTwoCreatureFromHandToBattlefield()
    {
        var vial = AetherVialFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vial);
        vial.SetZone(ZoneType.Battlefield);
        vial.Counters.Add(CounterType.Charge, 2);

        // Eligible: mv-2 bear (1G = mv 2).
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        // Ineligible: mv-4 hill giant (3R = mv 4).
        var giant = new Creature("Hill Giant", "3R", 3, 3);
        giant.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(giant);
        giant.SetZone(ZoneType.Hand);

        var ability = vial.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Grizzly Bears has mv 2 = number of charge counters — eligible to vial in");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        _alice.Zones.Hand.GetCards().Should().NotContain(bear);
        bear.Controller.Should().BeSameAs(_alice);

        giant.Zone.Should().Be(ZoneType.Hand,
            "Hill Giant has mv 4 ≠ 2 — must stay in hand");
    }

    [Fact]
    public void AetherVial_Activate_WithZeroCounters_OnlyManaValueZeroCreaturesEligible()
    {
        var vial = AetherVialFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vial);
        vial.SetZone(ZoneType.Battlefield);
        // No charge counters yet — target mv = 0.

        // Ineligible: mv-1 creature.
        var bear = new Creature("Bird Token", "G", 1, 1);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        // Eligible: mv-0 creature (free).
        var memnite = new Creature("Memnite", "", 1, 1);
        memnite.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(memnite);
        memnite.SetZone(ZoneType.Hand);

        var ability = vial.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        memnite.Zone.Should().Be(ZoneType.Battlefield,
            "Memnite has mv 0 = charge counters (0) — eligible");
        bear.Zone.Should().Be(ZoneType.Hand,
            "mv-1 creature is ineligible when counter count is 0");
    }

    [Fact]
    public void AetherVial_Activate_NoMatchingCreatureInHand_IsNoOp()
    {
        var vial = AetherVialFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vial);
        vial.SetZone(ZoneType.Battlefield);
        vial.Counters.Add(CounterType.Charge, 3);

        // Hand contains only a non-creature and a mv-2 creature — neither
        // matches the mv-3 target.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bolt);
        bolt.SetZone(ZoneType.Hand);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        var ability = vial.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var e in ability.Effects) e.Execute(); };

        act.Should().NotThrow(
            "no creature card with matching mv → no-op (CR 117.x — 'you may' with no valid target)");
        bolt.Zone.Should().Be(ZoneType.Hand);
        bear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bolt);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // CR 603.6a regression — ZoneService routing fires ETB triggers
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherVial_Activate_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(eventBus: bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var vial = AetherVialFactory.Create(
            alice, zoneService: zones, eventBus: bus, triggers: null);
        alice.Zones.Battlefield.AddCard(vial);
        vial.SetZone(ZoneType.Battlefield);
        vial.Counters.Add(CounterType.Charge, 2);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        var ability = vial.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, bear)
                && e.FromZone == ZoneType.Hand
                && e.ToZone == ZoneType.Battlefield,
            "hand → battlefield routes through ZoneService so ETB triggers on the placed creature fire (CR 603.6a)");
    }
}
