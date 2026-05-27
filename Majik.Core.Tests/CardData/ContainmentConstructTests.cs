using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ContainmentConstructFactory"/>.
///
/// Covers:
/// - Identity (name, type — Artifact + Creature, P/T, Construct
///   subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Trigger gated to Battlefield active zone (CR 113.6).
/// - Discard trigger fires on Hand → Graveyard CardMovedEvent for a
///   nonland card owned by Construct's controller.
/// - Trigger does NOT fire on:
///     * Land discards (CR 109.3 / 305.7).
///     * Opponent's discards.
///     * Non-discard hand→graveyard moves (well — every hand→GY move
///       qualifies as a discard at this level; sanity check that
///       non-hand→GY moves don't trigger).
/// - On resolve: discarded card moves Graveyard → Exile.
/// - On resolve: exiled card gets a RuntimeExileCast grant naming the
///   discarder and the printed mana cost.
/// - EOT (first Cleanup step seen after the discard) clears the grant.
/// </summary>
public class ContainmentConstructTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ContainmentConstruct_Identity()
    {
        var c = ContainmentConstructFactory.Create(_alice);

        c.Name.Should().Be("Containment Construct");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Creature).Should().BeTrue("Containment Construct is a creature");
        c.HasType(CardType.Artifact).Should().BeTrue("Containment Construct is also an artifact");
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the discard trigger is attached");
    }

    [Fact]
    public void ContainmentConstruct_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Containment Construct", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Containment Construct");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    [Fact]
    public void ContainmentConstruct_Trigger_GatedToBattlefield()
    {
        var c = ContainmentConstructFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "CR 113.6 — abilities on permanent cards function only from the battlefield");
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Discard trigger — fires on hand→graveyard for nonland controller-owned
    // -----------------------------------------------------------------------

    [Fact]
    public void Discard_NonLand_FiresTrigger_ExilesAndGrantsMayPlay()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var construct = ContainmentConstructFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(construct);
        construct.SetZone(ZoneType.Battlefield);

        // Alice discards a nonland card (Lightning Bolt-shape instant).
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bolt);
        bolt.SetZone(ZoneType.Hand);

        // Simulate the discard: hand → graveyard zone move + publish event.
        _alice.Zones.Hand.RemoveCard(bolt);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(bolt, ZoneType.Hand, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(1,
            "Construct's trigger fires on Alice's nonland discard");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Card moved Graveyard → Exile.
        bolt.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(bolt);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bolt);

        // Runtime exile-cast grant stamped for Alice with the bolt's
        // printed mana cost ({R}).
        bolt.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Containment Construct's controller is the named caster");
        bolt.RuntimeExileCastCost.Should().NotBeNull();
        bolt.RuntimeExileCastCost!.ToString().Should().Be(bolt.ManaCostValue.ToString());
    }

    // -----------------------------------------------------------------------
    // Land discard does NOT fire
    // -----------------------------------------------------------------------

    [Fact]
    public void Discard_Land_DoesNotFireTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var construct = ContainmentConstructFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(construct);
        construct.SetZone(ZoneType.Battlefield);

        // Alice discards a land — predicate rejects.
        var mountain = new Land("Mountain");
        mountain.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(mountain);
        mountain.SetZone(ZoneType.Hand);

        _alice.Zones.Hand.RemoveCard(mountain);
        _alice.Zones.Graveyard.AddCard(mountain);
        mountain.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(mountain, ZoneType.Hand, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(0,
            "land discards do not satisfy the printed 'nonland card' gate");
        mountain.Zone.Should().Be(ZoneType.Graveyard,
            "land stays in the graveyard");
    }

    // -----------------------------------------------------------------------
    // Opponent's discard does NOT fire
    // -----------------------------------------------------------------------

    [Fact]
    public void Discard_OpponentsCard_DoesNotFireTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var construct = ContainmentConstructFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(construct);
        construct.SetZone(ZoneType.Battlefield);

        // Bob discards one of his own nonland cards.
        var bobBolt = new Instant("Lightning Bolt", "{R}");
        bobBolt.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobBolt);
        bobBolt.SetZone(ZoneType.Hand);

        _bob.Zones.Hand.RemoveCard(bobBolt);
        _bob.Zones.Graveyard.AddCard(bobBolt);
        bobBolt.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(bobBolt, ZoneType.Hand, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU discard' is scoped to Construct's controller — " +
            "Bob's discards don't fire Alice's Construct");
        bobBolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Non-discard zone movement does NOT fire
    // -----------------------------------------------------------------------

    [Fact]
    public void NonDiscard_BattlefieldToGraveyard_DoesNotFireTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var construct = ContainmentConstructFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(construct);
        construct.SetZone(ZoneType.Battlefield);

        // A creature dying — battlefield → graveyard. Not a discard.
        var dying = new Creature("Bear", "{1}{G}", power: 2, toughness: 2);
        dying.SetOwner(_alice);
        bus.Publish(new CardMovedEvent(dying, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(0,
            "trigger gates on FromZone == Hand — battlefield→graveyard is not a discard");
    }

    // -----------------------------------------------------------------------
    // EOT — first Cleanup step seen after discard clears the grant
    // -----------------------------------------------------------------------

    [Fact]
    public void EOTCleanup_ClearsMayPlayGrant()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var construct = ContainmentConstructFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(construct);
        construct.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bolt);
        bolt.SetZone(ZoneType.Hand);

        _alice.Zones.Hand.RemoveCard(bolt);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(bolt, ZoneType.Hand, ZoneType.Graveyard));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bolt.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "grant present after the trigger resolves");

        // First Cleanup step after the discard — CR 514.2.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        bolt.RuntimeExileCastAllowedCaster.Should().BeNull(
            "the 'this turn' duration ended on the first Cleanup step");
        bolt.RuntimeExileCastCost.Should().BeNull();
    }
}
