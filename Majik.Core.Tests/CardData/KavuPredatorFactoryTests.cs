using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="KavuPredatorFactory"/> (Apocalypse, {1}{G}).
///
/// Covers:
/// - Identity (name, type Creature, subtype Kavu, P/T 2/2, mana cost,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Trample keyword marker (CR 702.19).
/// - Lifegain-punish trigger (CR 119.3 / 109.5 / 603.6a / 603.7): condition
///   matches an OPPONENT's strictly-positive life delta only — NOT the
///   controller's gain, NOT life loss.
/// - Resolution places "that many" +1/+1 counters on this creature — amount
///   captured via SetPendingGainAmount test hook AND via the event-bus
///   subscription auto-stamp.
/// - Trigger active only on the battlefield (CR 113.6).
/// </summary>
public class KavuPredatorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void KavuPredator_Identity()
    {
        var c = KavuPredatorFactory.Create(_alice);

        c.Name.Should().Be("Kavu Predator");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Kavu).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KavuPredator_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kavu Predator", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Kavu Predator");
        c.HasSubtype(CardSubtype.Kavu).Should().BeTrue();
    }

    [Fact]
    public void KavuPredator_HasTrampleKeyword()
    {
        var c = KavuPredatorFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Trample", "CR 702.19 — Trample is printed on Kavu Predator");
    }

    // -----------------------------------------------------------------------
    // Lifegain-punish trigger condition (CR 119.3 / 109.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void PunishTrigger_FiresForOpponentGain_NotControllerGain_NotLoss()
    {
        var c = KavuPredatorFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // An opponent (Bob) gaining life fires it (CR 109.5).
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 23), trigger).Should().BeTrue();
        // The controller's OWN gain does NOT (you are not your own opponent).
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 23), trigger).Should().BeFalse();
        // An opponent's life LOSS is not "gains life" (CR 119.3).
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 17), trigger).Should().BeFalse();
        // A zero delta is not a gain.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 20), trigger).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Counter resolution — "that many"
    // -----------------------------------------------------------------------

    [Fact]
    public void KavuPredator_OpponentGainsThree_PutsThreeCounters()
    {
        var c = KavuPredatorFactory.Create(_alice, eventBus: null, triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // Without a bus the amount slot is empty — stamp manually (shape path).
        KavuPredatorFactory.SetPendingGainAmount(c, 3);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "put 'that many' (3) +1/+1 counters on Kavu Predator");
    }

    [Fact]
    public void KavuPredator_BusWiring_StampsAmountAutomatically()
    {
        var bus = new EventBus();
        var c = KavuPredatorFactory.Create(_alice, eventBus: bus, triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // An opponent gains 5 — Kavu Predator's subscription stamps the slot.
        bus.Publish(new LifeChangedEvent(_bob, 20, 25));

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(5,
            "opponent gained 5 life → 5 +1/+1 counters");
    }

    [Fact]
    public void KavuPredator_ControllerGain_DoesNotStampAmount()
    {
        var bus = new EventBus();
        var c = KavuPredatorFactory.Create(_alice, eventBus: bus, triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // The CONTROLLER gaining life must not stamp the slot (CR 109.5).
        bus.Publish(new LifeChangedEvent(_alice, 20, 25));

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the controller's own life gain is not 'an opponent gains life'");
    }

    [Fact]
    public void KavuPredator_NoAmountStamp_CounterClauseNoOps()
    {
        var c = KavuPredatorFactory.Create(_alice, eventBus: null, triggers: null);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // End-to-end: bus-fired opponent gain auto-queues + resolves the trigger.
    // -----------------------------------------------------------------------

    [Fact]
    public void KavuPredator_BusFiredOpponentGain_QueuesAndResolves()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var c = KavuPredatorFactory.Create(_alice, eventBus: bus, triggers: triggers);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        bus.Publish(new LifeChangedEvent(_bob, 20, 22));

        triggers.PendingCount.Should().Be(1, "an opponent gaining life queues the trigger");
        triggers.PutPendingTriggersOnStack(_alice);
        while (true)
        {
            var top = stack.Pop();
            if (top == null) break;
            top.Resolve();
        }

        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "opponent gained 2 → 2 +1/+1 counters placed on resolution");
    }

    [Fact]
    public void KavuPredator_PunishTrigger_OnlyActiveOnBattlefield()
    {
        var c = KavuPredatorFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
