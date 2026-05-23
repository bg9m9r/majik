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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Boros Reckoner (Gatecrash, {R/W}{R/W}{R/W}).
///
/// Covers:
///   - Card identity (name, hybrid mana cost, P/T, Minotaur Wizard).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - First strike keyword marker readable by
///     <see cref="CombatAbilities.HasFirstStrike"/>.
///   - Damage-received trigger structure (active on battlefield).
///   - Mechanic: 3 damage to Boros Reckoner → 3 damage to redirect target.
///   - Mechanic: 5 damage to Boros Reckoner → 5 damage to redirect target.
///   - 0-damage event does not fire the trigger (predicate gate).
///   - Damage to a different creature does NOT fire the trigger.
/// </summary>
public class BorosReckonerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BorosReckoner_Is_MinotaurWizard_3_3_AtHybridCost()
    {
        var rec = BorosReckonerFactory.Create(_alice);

        rec.Name.Should().Be("Boros Reckoner");
        rec.ManaCost.Should().Be("{R/W}{R/W}{R/W}");
        rec.HasType(CardType.Creature).Should().BeTrue();
        rec.HasSubtype(CardSubtype.Minotaur).Should().BeTrue();
        rec.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        rec.BasePower.Should().Be(3);
        rec.BaseToughness.Should().Be(3);
        rec.Owner.Should().BeSameAs(_alice);
        rec.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasFirstStrike(rec).Should().BeTrue(
            "First strike keyword marker is wired (CR 702.7)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BorosReckoner()
    {
        var card = NamedCardFactory.Create("Boros Reckoner", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Boros Reckoner");
        card.HasSubtype(CardSubtype.Minotaur).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "damage-received trigger is wired");
        card.Abilities.OfType<KeywordAbility>().Should().ContainSingle(k =>
            string.Equals(k.Keyword, "First strike", System.StringComparison.OrdinalIgnoreCase),
            "First strike keyword marker is wired");
    }

    [Fact]
    public void BorosReckoner_HasDamageReceivedTrigger_ActiveOnBattlefieldOnly()
    {
        var rec = BorosReckonerFactory.Create(_alice);

        var triggers = rec.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    [Fact]
    public void BorosReckoner_Takes3Damage_Deals3ToTargetPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rec = BorosReckonerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rec);
        rec.SetZone(ZoneType.Battlefield);

        // Target Bob with the redirected damage.
        var trig = rec.Abilities.OfType<BorosReckonerTrigger>().Single();
        trig.RedirectTarget = _bob;

        // Some other source deals 3 damage to Boros Reckoner.
        var blaster = new Creature("Blaster", "{2}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: rec,
            targetPlayer: null,
            amount: 3,
            damageType: DamageType.Ability));

        triggers.PutPendingTriggersOnStack(_alice);
        var queued = stack.Pop();
        queued.Should().NotBeNull("the damage-received trigger should queue");
        queued!.Resolve();

        _bob.LifeTotal.Should().Be(17,
            "Boros Reckoner redirects the 3 damage it took to the chosen player");
    }

    [Fact]
    public void BorosReckoner_Takes5Damage_Deals5ToTargetPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rec = BorosReckonerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rec);
        rec.SetZone(ZoneType.Battlefield);

        var trig = rec.Abilities.OfType<BorosReckonerTrigger>().Single();
        trig.RedirectTarget = _bob;

        var blaster = new Creature("Blaster", "{4}{R}", 5, 5) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: rec,
            targetPlayer: null,
            amount: 5,
            damageType: DamageType.Ability));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(15,
            "redirect amount scales with the damage dealt to Boros Reckoner");
    }

    [Fact]
    public void BorosReckoner_ZeroDamage_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rec = BorosReckonerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rec);
        rec.SetZone(ZoneType.Battlefield);

        var trig = rec.Abilities.OfType<BorosReckonerTrigger>().Single();
        trig.RedirectTarget = _bob;

        // A 0-damage event must not fire the trigger — CR 119.4
        // ("If a source would deal 0 damage, it does not deal damage").
        // Boros Reckoner publishes a 0-damage shim event here to assert
        // the predicate gate.
        var blaster = new Creature("Blaster", "{R}", 0, 0) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: rec,
            targetPlayer: null,
            amount: 0,
            damageType: DamageType.Ability));

        triggers.PendingCount.Should().Be(0,
            "0-damage instances don't trigger the damage-received ability");
        _bob.LifeTotal.Should().Be(20, "no redirect, no life loss");
    }

    [Fact]
    public void BorosReckoner_DamageToOtherCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rec = BorosReckonerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rec);
        rec.SetZone(ZoneType.Battlefield);

        var trig = rec.Abilities.OfType<BorosReckonerTrigger>().Single();
        trig.RedirectTarget = _bob;

        // Damage goes to a different creature — Boros Reckoner's trigger
        // is scoped to "Boros Reckoner is dealt damage" (CR 603.1).
        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice, Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(other);
        other.SetZone(ZoneType.Battlefield);

        var blaster = new Creature("Blaster", "{2}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: other,
            targetPlayer: null,
            amount: 3,
            damageType: DamageType.Ability));

        triggers.PendingCount.Should().Be(0,
            "trigger only fires when Boros Reckoner itself is dealt damage");
        _bob.LifeTotal.Should().Be(20);
    }
}
