using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Phyrexian Obliterator (New Phyrexia, {B}{B}{B}{B}).
///
/// Creature — Phyrexian Horror 5/5. Oracle text (Scryfall verified):
///   "Trample
///    Whenever a source deals damage to this creature, that source's
///    controller sacrifices that many permanents of their choice."
///
/// Covers:
///   - Card identity (name, quadruple-black cost, P/T, Phyrexian Horror).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trample keyword marker readable by
///     <see cref="CombatAbilities.HasTrample"/>.
///   - Damage-received trigger structure (active on battlefield only).
///   - Mechanic: a creature deals 3 damage to the Obliterator → that
///     creature's controller sacrifices 3 permanents.
///   - Mechanic: the sacrifice count scales with the damage amount.
///   - Sacrifice clamps to the number of permanents controlled when the
///     controller has fewer than the damage amount.
///   - 0-damage instances do NOT fire the trigger (predicate gate, CR 119.4).
///   - Damage to a different creature does NOT fire the trigger.
/// </summary>
[Trait("Color", "B")]
public class PhyrexianObliteratorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void PhyrexianObliterator_Is_PhyrexianHorror_5_5_AtQuadBlackCost()
    {
        var ob = PhyrexianObliteratorFactory.Create(_alice);

        ob.Name.Should().Be("Phyrexian Obliterator");
        ob.ManaCost.Should().Be("{B}{B}{B}{B}");
        ob.HasType(CardType.Creature).Should().BeTrue();
        ob.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        ob.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        ob.BasePower.Should().Be(5);
        ob.BaseToughness.Should().Be(5);
        ob.Owner.Should().BeSameAs(_alice);
        ob.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasTrample(ob).Should().BeTrue(
            "Trample keyword marker is wired (CR 702.19)");
    }
    [Fact]
    public void PhyrexianObliterator_HasDamageReceivedTrigger_ActiveOnBattlefieldOnly()
    {
        var ob = PhyrexianObliteratorFactory.Create(_alice);

        var triggers = ob.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    [Fact]
    public void PhyrexianObliterator_Takes3Damage_SourceControllerSacrifices3Permanents()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        // Bob controls the damaging source and four permanents to sacrifice.
        var blaster = new Creature("Blaster", "{2}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(blaster);
        blaster.SetZone(ZoneType.Battlefield);

        var p1 = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var p2 = new Creature("Llanowar Elves", "{G}", 1, 1) { Owner = _bob, Controller = _bob };
        var p3 = new Creature("Forest Bear", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        foreach (var p in new[] { p1, p2, p3 })
        {
            _bob.Zones.Battlefield.AddCard(p);
            p.SetZone(ZoneType.Battlefield);
        }

        var beforeCount = _bob.Zones.Battlefield.GetCards().Count();

        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: ob,
            targetPlayer: null,
            amount: 3,
            damageType: DamageType.Combat));

        triggers.PutPendingTriggersOnStack(_alice);
        var queued = stack.Pop();
        queued.Should().NotBeNull("the damage-received sacrifice trigger should queue");
        queued!.Resolve();

        var afterCount = _bob.Zones.Battlefield.GetCards().Count();
        (beforeCount - afterCount).Should().Be(3,
            "the source's controller sacrifices that many (3) permanents (CR 701.16)");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3,
            "the sacrificed permanents go to their owner's graveyard");
    }

    [Fact]
    public void PhyrexianObliterator_Takes1Damage_SourceControllerSacrifices1Permanent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        var blaster = new Creature("Pinger", "{R}", 1, 1) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(blaster);
        blaster.SetZone(ZoneType.Battlefield);

        var p1 = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var p2 = new Creature("Llanowar Elves", "{G}", 1, 1) { Owner = _bob, Controller = _bob };
        foreach (var p in new[] { p1, p2 })
        {
            _bob.Zones.Battlefield.AddCard(p);
            p.SetZone(ZoneType.Battlefield);
        }

        var beforeCount = _bob.Zones.Battlefield.GetCards().Count();

        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: ob,
            targetPlayer: null,
            amount: 1,
            damageType: DamageType.Combat));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        (beforeCount - _bob.Zones.Battlefield.GetCards().Count()).Should().Be(1,
            "sacrifice count scales with the damage dealt to the Obliterator");
    }

    [Fact]
    public void PhyrexianObliterator_SacrificeClampsToControlledPermanents()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        // Bob's only permanent is the damaging source itself.
        var blaster = new Creature("Lonely Blaster", "{4}{R}", 5, 5) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(blaster);
        blaster.SetZone(ZoneType.Battlefield);

        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: ob,
            targetPlayer: null,
            amount: 5,
            damageType: DamageType.Combat));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty(
            "with only one permanent, Bob sacrifices all he controls — the count clamps to what's available (CR 701.16e)");
    }

    [Fact]
    public void PhyrexianObliterator_ZeroDamage_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        var blaster = new Creature("Blaster", "{R}", 0, 0) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(blaster);
        blaster.SetZone(ZoneType.Battlefield);

        // CR 119.4 — a source that would deal 0 damage deals no damage.
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: ob,
            targetPlayer: null,
            amount: 0,
            damageType: DamageType.Combat));

        triggers.PendingCount.Should().Be(0,
            "0-damage instances don't fire the damage-received trigger");
        _bob.Zones.Battlefield.GetCards().Should().HaveCount(1, "no sacrifice");
    }

    [Fact]
    public void PhyrexianObliterator_DamageToOtherCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(other);
        other.SetZone(ZoneType.Battlefield);

        var blaster = new Creature("Blaster", "{2}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(blaster);
        blaster.SetZone(ZoneType.Battlefield);

        // Damage to a different creature — Obliterator's trigger is scoped
        // to "this creature" (CR 603.1).
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: other,
            targetPlayer: null,
            amount: 3,
            damageType: DamageType.Combat));

        triggers.PendingCount.Should().Be(0,
            "trigger only fires when the Obliterator itself is dealt damage");
        _bob.Zones.Battlefield.GetCards().Should().HaveCount(1, "no sacrifice");
    }
}
