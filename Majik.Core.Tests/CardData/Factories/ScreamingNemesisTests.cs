using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Screaming Nemesis (Duskmourn: House of Horror, {2}{R}).
///
/// Oracle text (verified against Scryfall, set dsk #157):
///   "Haste
///    Whenever this creature is dealt damage, it deals that much damage to
///    any other target. If a player is dealt damage this way, they can't
///    gain life for the rest of the game."
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Spirit subtype).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Haste keyword marker readable by <see cref="CombatAbilities.HasHaste"/>.
///   - Damage-received trigger structure (active on battlefield).
///   - Mechanic: damage to Screaming Nemesis → equal damage to redirect target.
///   - Mechanic: when the redirect target is a player, that player can't gain
///     life for the rest of the game (CR 614 — permanent, non-expiring).
///   - 0-damage event does not fire the trigger (predicate gate).
///   - Damage to a different creature does NOT fire the trigger.
/// </summary>
[Trait("Color", "R")]
public class ScreamingNemesisTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ScreamingNemesis_Is_Spirit_3_3_WithHaste()
    {
        var nem = ScreamingNemesisFactory.Create(_alice);

        nem.Name.Should().Be("Screaming Nemesis");
        nem.ManaCost.Should().Be("{2}{R}");
        nem.HasType(CardType.Creature).Should().BeTrue();
        nem.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        nem.BasePower.Should().Be(3);
        nem.BaseToughness.Should().Be(3);
        nem.Owner.Should().BeSameAs(_alice);
        nem.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasHaste(nem).Should().BeTrue(
            "Haste keyword marker is wired (CR 702.10)");
    }
    [Fact]
    public void ScreamingNemesis_HasDamageReceivedTrigger_ActiveOnBattlefieldOnly()
    {
        var nem = ScreamingNemesisFactory.Create(_alice);

        var triggers = nem.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    [Fact]
    public void ScreamingNemesis_Takes3Damage_Deals3ToTargetCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nem = ScreamingNemesisFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nem);
        nem.SetZone(ZoneType.Battlefield);

        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var trig = nem.Abilities.OfType<ScreamingNemesisFactory.ScreamingNemesisTrigger>().Single();
        trig.RedirectTarget = victim;

        var blaster = new Creature("Blaster", "{2}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: nem,
            targetPlayer: null,
            amount: 3,
            damageType: DamageType.Ability));

        triggers.PutPendingTriggersOnStack(_alice);
        var queued = stack.Pop();
        queued.Should().NotBeNull("the damage-received trigger should queue");
        queued!.Resolve();

        victim.Damage.Should().Be(3,
            "Screaming Nemesis redirects the 3 damage it took to the chosen creature");
    }

    [Fact]
    public void ScreamingNemesis_RedirectToPlayer_DealsDamageAndLocksLifeGainForRestOfGame()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Bob needs a replacement bus attached so the "can't gain life"
        // static can route through Player.GainLife (CR 614).
        _bob.AttachReplacementBus(new ReplacementBus());

        var nem = ScreamingNemesisFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nem);
        nem.SetZone(ZoneType.Battlefield);

        var trig = nem.Abilities.OfType<ScreamingNemesisFactory.ScreamingNemesisTrigger>().Single();
        trig.RedirectTarget = _bob;

        var blaster = new Creature("Blaster", "{4}{R}", 5, 5) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: nem,
            targetPlayer: null,
            amount: 5,
            damageType: DamageType.Ability));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(15,
            "redirected damage scales with the damage dealt to Screaming Nemesis");

        // CR 614 — Bob "can't gain life for the rest of the game": the gain
        // is rewritten to zero, permanently (no end-of-turn expiry).
        _bob.GainLife(10);
        _bob.LifeTotal.Should().Be(15, "the can't-gain-life lock prevents the gain");
    }

    [Fact]
    public void ScreamingNemesis_RedirectToCreature_DoesNotLockAnyPlayerLifeGain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        _bob.AttachReplacementBus(new ReplacementBus());

        var nem = ScreamingNemesisFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nem);
        nem.SetZone(ZoneType.Battlefield);

        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var trig = nem.Abilities.OfType<ScreamingNemesisFactory.ScreamingNemesisTrigger>().Single();
        trig.RedirectTarget = victim;

        var blaster = new Creature("Blaster", "{2}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: nem,
            targetPlayer: null,
            amount: 3,
            damageType: DamageType.Ability));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // No player was dealt damage this way — life gain is unaffected.
        _bob.GainLife(4);
        _bob.LifeTotal.Should().Be(24, "creature redirect does not lock a player's life gain");
    }

    [Fact]
    public void ScreamingNemesis_ZeroDamage_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nem = ScreamingNemesisFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nem);
        nem.SetZone(ZoneType.Battlefield);

        var trig = nem.Abilities.OfType<ScreamingNemesisFactory.ScreamingNemesisTrigger>().Single();
        trig.RedirectTarget = _bob;

        var blaster = new Creature("Blaster", "{R}", 0, 0) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: blaster,
            sourcePlayer: null,
            targetCard: nem,
            targetPlayer: null,
            amount: 0,
            damageType: DamageType.Ability));

        triggers.PendingCount.Should().Be(0,
            "0-damage instances don't trigger the damage-received ability (CR 119.4)");
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void ScreamingNemesis_DamageToOtherCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nem = ScreamingNemesisFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(nem);
        nem.SetZone(ZoneType.Battlefield);

        var trig = nem.Abilities.OfType<ScreamingNemesisFactory.ScreamingNemesisTrigger>().Single();
        trig.RedirectTarget = _bob;

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
            "trigger only fires when Screaming Nemesis itself is dealt damage (CR 603.1)");
        _bob.LifeTotal.Should().Be(20);
    }
}
