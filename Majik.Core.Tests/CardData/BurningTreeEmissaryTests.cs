using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BurningTreeEmissaryFactory"/> (Dragon's Maze
/// {R/G}{R/G}). Oracle: "When this creature enters, add {R}{G}."
///
/// Covers:
/// - Identity (Creature — Human Shaman 2/2 at {R/G}{R/G}, Cat is NOT a subtype).
/// - NamedCardFactory dispatch.
/// - ETB trigger fires and resolves into {R}{G} in the controller's mana pool.
/// - ETB trigger fires only on Emissary itself, not on other ETBs.
/// </summary>
public class BurningTreeEmissaryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void BurningTreeEmissary_Identity_HumanShaman_2_2()
    {
        var bte = BurningTreeEmissaryFactory.Create(_alice);

        bte.Name.Should().Be("Burning-Tree Emissary");
        bte.ManaCost.Should().Be("{R/G}{R/G}");
        bte.HasType(CardType.Creature).Should().BeTrue();
        bte.HasSubtype(CardSubtype.Human).Should().BeTrue("Burning-Tree Emissary is a Human (CR 205.3m)");
        bte.HasSubtype(CardSubtype.Shaman).Should().BeTrue("Burning-Tree Emissary is also a Shaman (CR 205.3m)");
        bte.Power.Should().Be(2);
        bte.Toughness.Should().Be(2);
        bte.Owner.Should().BeSameAs(_alice);
        bte.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BurningTreeEmissary_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Burning-Tree Emissary", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Burning-Tree Emissary");
        c.ManaCost.Should().Be("{R/G}{R/G}");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    // -------------------------------------------------------------------
    // ETB trigger — add {R}{G} to controller's mana pool
    // -------------------------------------------------------------------

    [Fact]
    public void BurningTreeEmissary_Etb_AddsRedGreenToManaPool()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bte = BurningTreeEmissaryFactory.Create(_alice, triggers);

        // Simulate Emissary entering — publish CardMovedEvent → Battlefield.
        _alice.Zones.Battlefield.AddCard(bte);
        bte.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(bte, ZoneType.Hand, ZoneType.Battlefield));

        // Trigger should be pending.
        triggers.PendingCount.Should().Be(1, "ETB queues one triggered ability");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 106.4 — mana pool should now contain {R}{G}.
        _alice.ManaPool.Red.Should().Be(1, "ETB adds one red mana");
        _alice.ManaPool.Green.Should().Be(1, "ETB adds one green mana");
        _alice.ManaPool.Total.Should().Be(2, "exactly {R}{G} — no extra mana");
    }

    [Fact]
    public void BurningTreeEmissary_Etb_DoesNotFireForOtherCardsEntering()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bte = BurningTreeEmissaryFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bte);
        bte.SetZone(ZoneType.Battlefield);

        // Some other creature ETBing should not queue Emissary's trigger.
        var other = new Creature("Llanowar Elves", "G", 1, 1);
        other.SetOwner(_alice);
        other.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(other);
        other.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "Triggers.OnEnterBattlefieldSelf matches the SELF card only, not arbitrary ETBs");
    }

    [Fact]
    public void BurningTreeEmissary_HasEtbTriggeredAbility()
    {
        var bte = BurningTreeEmissaryFactory.Create(_alice);

        var etb = bte.Abilities.OfType<TriggeredAbility>().SingleOrDefault();
        etb.Should().NotBeNull("Burning-Tree Emissary has one triggered ability (ETB)");
    }

    [Fact]
    public void BurningTreeEmissary_Etb_TwoConsecutiveEntries_StackTwoTriggers()
    {
        // Each ETB queues a fresh trigger — two Emissaries in a row should
        // each independently produce {R}{G}, totaling {R}{R}{G}{G}.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bte1 = BurningTreeEmissaryFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bte1);
        bte1.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(bte1, ZoneType.Hand, ZoneType.Battlefield));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var bte2 = BurningTreeEmissaryFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bte2);
        bte2.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(bte2, ZoneType.Hand, ZoneType.Battlefield));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Red.Should().Be(2, "two Emissaries each added {R}");
        _alice.ManaPool.Green.Should().Be(2, "two Emissaries each added {G}");
    }
}
