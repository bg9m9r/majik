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
/// Unit tests for <see cref="PhyrexianObliteratorFactory"/>
/// (New Phyrexia, {B}{B}{B}{B}).
///
/// Creature — Horror 5/5. Oracle text:
///   "Trample
///    Whenever a source deals damage to Phyrexian Obliterator, that
///    source's controller sacrifices that many permanents."
///
/// Covers:
///   - Identity (Horror, {B}{B}{B}{B}, 5/5).
///   - NamedCardFactory dispatch.
///   - Trample keyword marker readable by CombatAbilities.HasTrample.
///   - Damage-received trigger structure (active on battlefield only).
///   - Mechanic: 3 damage from Bob's source → Bob sacrifices 3 permanents.
///   - Mechanic: sacrifice count scales with damage amount.
///   - 0-damage event does not fire the trigger.
///   - Damage from a sourceless / player-source event still routes
///     through SourcePlayer (player-as-source handling).
/// </summary>
public class PhyrexianObliteratorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void PhyrexianObliterator_Identity()
    {
        var ob = PhyrexianObliteratorFactory.Create(_alice);

        ob.Name.Should().Be("Phyrexian Obliterator");
        ob.ManaCost.Should().Be("{B}{B}{B}{B}");
        ob.HasType(CardType.Creature).Should().BeTrue();
        ob.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        ob.BasePower.Should().Be(5);
        ob.BaseToughness.Should().Be(5);
        ob.Owner.Should().BeSameAs(_alice);
        ob.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasTrample(ob).Should().BeTrue(
            "CR 702.19 — Trample marker is wired");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PhyrexianObliterator()
    {
        var card = NamedCardFactory.Create("Phyrexian Obliterator", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Phyrexian Obliterator");
        card.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(5);
        ((Creature)card).BaseToughness.Should().Be(5);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "damage-received trigger is wired");
        card.Abilities.OfType<KeywordAbility>().Should().ContainSingle(k =>
            string.Equals(k.Keyword, "Trample", System.StringComparison.OrdinalIgnoreCase),
            "Trample keyword marker is wired");
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
    public void PhyrexianObliterator_Takes3Damage_BobSacrifices3Permanents()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerMgr = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, triggerMgr, agentSelector: null);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        // Bob controls four permanents — three creatures + one
        // attacker. The Obliterator-trigger fires off the damage
        // sourced from one of Bob's creatures and forces Bob (not
        // Alice) to sacrifice equal-to-damage permanents.
        var brute = new Creature("Brute", "{2}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        var grunt1 = new Creature("Grunt 1", "{R}", 1, 1) { Owner = _bob, Controller = _bob };
        var grunt2 = new Creature("Grunt 2", "{R}", 1, 1) { Owner = _bob, Controller = _bob };
        var grunt3 = new Creature("Grunt 3", "{R}", 1, 1) { Owner = _bob, Controller = _bob };

        foreach (var c in new[] { brute, grunt1, grunt2, grunt3 })
        {
            _bob.Zones.Battlefield.AddCard(c);
            c.SetZone(ZoneType.Battlefield);
        }

        bus.Publish(new DamageDealtEvent(
            sourceCard: brute,
            sourcePlayer: null,
            targetCard: ob,
            targetPlayer: null,
            amount: 3,
            damageType: DamageType.Combat));

        triggerMgr.PutPendingTriggersOnStack(_alice);
        var queued = stack.Pop();
        queued.Should().NotBeNull("damage-received trigger should queue");
        queued!.Resolve();

        _bob.Zones.Battlefield.GetCards()
            .Should().HaveCount(1,
                "Bob started with 4 permanents and sacrifices 3 — one remains");
        _bob.Zones.Graveyard.GetCards()
            .Should().HaveCount(3,
                "sacrifice routes the picks to Bob's graveyard via Fx.Sacrifice");
    }

    [Fact]
    public void PhyrexianObliterator_Takes5Damage_BobSacrificesUpTo5_OrAllAvailable()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerMgr = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, triggerMgr, agentSelector: null);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        // Bob has only 2 permanents — sacrifice halts when the
        // battlefield is empty (printed text resolves as much as
        // possible per CR 608.2c — instructed-but-impossible clause).
        var source = new Creature("Spitter", "{3}{R}", 5, 1) { Owner = _bob, Controller = _bob };
        var lonely = new Creature("Lonely", "{R}", 1, 1) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(source); source.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(lonely); lonely.SetZone(ZoneType.Battlefield);

        bus.Publish(new DamageDealtEvent(
            sourceCard: source,
            sourcePlayer: null,
            targetCard: ob,
            targetPlayer: null,
            amount: 5,
            damageType: DamageType.Combat));

        triggerMgr.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty(
            "Bob only had 2 permanents to give — both sacrificed");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2,
            "the loop terminates when no candidates remain");
    }

    [Fact]
    public void PhyrexianObliterator_ZeroDamage_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerMgr = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, triggerMgr, agentSelector: null);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        var grunt = new Creature("Grunt", "{R}", 1, 1) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(grunt); grunt.SetZone(ZoneType.Battlefield);

        // CR 119.4 — "If a source would deal 0 damage, it does not
        // deal damage." Prevention shields can also reduce damage to
        // zero. The trigger's amount-gate rejects 0-amount events.
        bus.Publish(new DamageDealtEvent(
            sourceCard: grunt,
            sourcePlayer: null,
            targetCard: ob,
            targetPlayer: null,
            amount: 0,
            damageType: DamageType.Combat));

        triggerMgr.PendingCount.Should().Be(0,
            "0-amount damage events don't fire the sacrifice trigger");
        _bob.Zones.Battlefield.GetCards().Should().HaveCount(1,
            "no sacrifice happened");
    }

    [Fact]
    public void PhyrexianObliterator_DamageToOtherCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerMgr = new TriggerManager(stack, bus);

        var ob = PhyrexianObliteratorFactory.Create(_alice, triggerMgr, agentSelector: null);
        _alice.Zones.Battlefield.AddCard(ob);
        ob.SetZone(ZoneType.Battlefield);

        // Another Alice creature soaks the damage — the trigger only
        // fires when the Obliterator itself is the recipient.
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(bear); bear.SetZone(ZoneType.Battlefield);

        var bolt = new Creature("Slinger", "{R}", 1, 1) { Owner = _bob, Controller = _bob };
        bus.Publish(new DamageDealtEvent(
            sourceCard: bolt,
            sourcePlayer: null,
            targetCard: bear,
            targetPlayer: null,
            amount: 2,
            damageType: DamageType.Combat));

        triggerMgr.PendingCount.Should().Be(0,
            "trigger only fires on damage to the Obliterator itself");
    }
}
