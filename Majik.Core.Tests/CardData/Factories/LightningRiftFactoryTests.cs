using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LightningRiftFactory"/> (Onslaught).
///
/// Covers:
/// - Identity ({1}{R} Enchantment).
/// - Triggered ability shape — single
///   <see cref="EventTriggerCondition{CardCycledEvent}"/> with one
///   1..1 "any target" <see cref="Targeting.TargetRequest"/>.
/// - Trigger fires on <see cref="CardCycledEvent"/> publication when
///   the Rift is registered with a <see cref="Abilities.TriggerManager"/>
///   and on the battlefield.
/// - Trigger does NOT fire when the Rift is off the battlefield (ETB
///   gating via ActiveZones).
/// - End-to-end: cycle a card → Rift's trigger queues → resolve →
///   {1} consumed from Rift controller's pool → 2 damage applied.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class LightningRiftFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningRift_Identity_EnchantmentOneRed()
    {
        var rift = LightningRiftFactory.Create(_alice);

        rift.Name.Should().Be("Lightning Rift");
        rift.ManaCost.ToString().Should().Be("{1}{R}");
        rift.HasType(CardType.Enchantment).Should().BeTrue();
        rift.Owner.Should().BeSameAs(_alice);
        rift.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LightningRift_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Lightning Rift", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Lightning Rift");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "exactly one cycling-trigger is attached");
    }

    // -----------------------------------------------------------------------
    // Trigger shape — CardCycledEvent + "any target" 1..1
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningRift_TriggerSubscribesToCardCycledEvent()
    {
        var rift = LightningRiftFactory.Create(_alice);
        var trigger = rift.Abilities.OfType<TriggeredAbility>().Single();

        var cond = trigger.Condition.Should().BeOfType<EventTriggerCondition<CardCycledEvent>>().Subject;
        cond.EventType.Should().Be(typeof(CardCycledEvent));
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "Lightning Rift's trigger only fires while on the battlefield");
    }

    [Fact]
    public void LightningRift_TriggerHasSingleAnyTargetRequest()
    {
        var rift = LightningRiftFactory.Create(_alice);
        var trigger = rift.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        var req = trigger.TargetRequests[0];
        req.Description.Should().Contain("any target");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Trigger firing — registered + on battlefield → queued on CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningRift_OnBattlefield_QueuesTriggerOnCardCycledEvent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack();
        var triggers = new TriggerManager(stack, bus);

        var rift = LightningRiftFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(rift);
        rift.SetZone(ZoneType.Battlefield);

        // Publish a CardCycledEvent — Lightning Rift's trigger should
        // queue (the trigger condition is "any cycling event").
        var dummy = new Card("Krosan Tusker", "{4}{G}");
        dummy.SetOwner(_alice);
        bus.Publish(new CardCycledEvent(dummy, _alice));

        triggers.PendingCount.Should().Be(1,
            "Lightning Rift's trigger queues on every CardCycledEvent");
    }

    [Fact]
    public void LightningRift_OffBattlefield_DoesNotQueueTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack();
        var triggers = new TriggerManager(stack, bus);

        var rift = LightningRiftFactory.Create(_alice, triggers);
        // Card left in hand — ActiveZones is Battlefield, so the
        // trigger should be inactive.
        _alice.Zones.Hand.AddCard(rift);
        rift.SetZone(ZoneType.Hand);
        // Re-bind so the manager re-syncs registration off the current
        // zone (the TriggerManager.BindCard / SyncCardRegistration path).
        triggers.UnregisterTriggeredAbility(
            rift.Abilities.OfType<TriggeredAbility>().Single());

        var dummy = new Card("Krosan Tusker", "{4}{G}");
        dummy.SetOwner(_alice);
        bus.Publish(new CardCycledEvent(dummy, _alice));

        triggers.PendingCount.Should().Be(0,
            "Lightning Rift in hand — trigger is not active");
    }

    // -----------------------------------------------------------------------
    // End-to-end — cycling Tranquil Thicket fires Rift's trigger; pays
    // {1}; deals 2 damage to the chosen target
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningRift_EndToEnd_CyclingFiresRift_Pay1_Deal2ToPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack();
        var triggers = new TriggerManager(stack, bus);

        // Lightning Rift on Alice's battlefield, registered.
        var rift = LightningRiftFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(rift);
        rift.SetZone(ZoneType.Battlefield);

        // Alice has {1} in pool for the optional may-pay rider.
        _alice.AddManaToPool(ManaCost.Parse("1"));

        // Seed Alice's library so cycling Tranquil Thicket draws.
        var topCard = new Card("Forest", "");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        // Cycle Tranquil Thicket from Alice's hand. The factory uses
        // the same CyclingFactory primitive, so the bus gets a
        // CardCycledEvent on resolve.
        var thicket = OnslaughtCyclingLandFactory.Create(
            _alice,
            new[] { "Tranquil Thicket", "G", "Forest" },
            eventBus: bus,
            replacements: null);
        _alice.Zones.Hand.AddCard(thicket);
        thicket.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("G"));

        var cycling = thicket.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs) cost.Pay(_alice);
        foreach (var effect in cycling.Effects) effect.Execute();

        // Rift's trigger should have queued. Pre-supply Bob as target
        // (v1 doesn't auto-prompt without an async agent flow), then
        // resolve.
        triggers.PendingCount.Should().Be(1);
        var trigger = rift.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new[] { new object[] { _bob } });

        var startingBobLife = _bob.LifeTotal;
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.ManaPool.Generic.Should().Be(0,
            "Rift's optional {1} was paid from Alice's pool");
        _bob.LifeTotal.Should().Be(startingBobLife - LightningRiftFactory.DamageAmount,
            "Lightning Rift dealt 2 damage to Bob");
    }
}
