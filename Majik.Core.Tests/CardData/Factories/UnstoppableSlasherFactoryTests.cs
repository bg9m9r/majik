using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="UnstoppableSlasherFactory"/> — Creature — Zombie
/// Assassin {2}{B} 2/3 (Scryfall, verified 2026-06-23):
///   "Deathtouch
///    Whenever this creature deals combat damage to a player, they lose half
///    their life, rounded up.
///    When this creature dies, if it had no counters on it, return it to the
///    battlefield tapped under its owner's control with two stun counters on
///    it."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (one *_Identity assert: cost / P-T / subtypes — non-vanilla).
///   - Deathtouch keyword marker.
///   - Combat-damage-to-a-player trigger: gates on damage to a player from
///     this creature; resolution drains ceil(life / 2) (rounds up).
///   - Dies trigger: returns the creature tapped with two stun counters when
///     it had no counters at death; intervening-if suppresses it when a
///     counter was present (any counter type), including the stun counters
///     placed by its own return.
/// </summary>
[Trait("Color", "B")]
public class UnstoppableSlasherFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats)
    // -----------------------------------------------------------------------

    [Fact]
    public void UnstoppableSlasher_Identity_ZombieAssassin_2_3_At2B()
    {
        var c = UnstoppableSlasherFactory.Create(_alice);

        c.Name.Should().Be("Unstoppable Slasher");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Assassin).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void UnstoppableSlasher_HasDeathtouch()
    {
        var c = UnstoppableSlasherFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Deathtouch");
    }

    // -----------------------------------------------------------------------
    // Combat damage to a player → lose half life, rounded up
    // -----------------------------------------------------------------------

    // The combat-damage trigger is the one whose active zone is Battlefield-only
    // (the dies trigger is also active in the Graveyard). Selecting by active
    // zones avoids invoking IsTriggered (whose predicate has a capture
    // side-effect) during the probe.
    private static TriggeredAbility CombatTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Count == 1 && t.ActiveZones.Contains(ZoneType.Battlefield));

    [Fact]
    public void CombatDamageToPlayer_DrainsHalfRoundedUp_FromLiveLife()
    {
        var slasher = UnstoppableSlasherFactory.Create(_alice);
        slasher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(slasher);

        // Bob at 15 (odd). ceil(15 / 2) = 8 → ends at 7.
        _bob.LoseLife(5);
        _bob.LifeTotal.Should().Be(15);

        var trigger = CombatTrigger(slasher);
        trigger.IsTriggered(new CombatDamageDealtEvent(slasher, _bob, amount: 2))
            .Should().BeTrue();
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(7,
            "Bob at 15 loses ceil(15 / 2) = 8 (printed 'half, rounded up').");
    }

    [Fact]
    public void CombatDamageToCreature_DoesNotTrigger()
    {
        var slasher = UnstoppableSlasherFactory.Create(_alice);
        slasher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(slasher);
        var blocker = new Creature("Wall", "{1}", 0, 4) { Owner = _bob, Controller = _bob };

        var trigger = CombatTrigger(slasher);

        // Damage to a creature, not a player.
        trigger.IsTriggered(new CombatDamageDealtEvent(slasher, (Majik.Core.Cards.ICard?)blocker, amount: 2))
            .Should().BeFalse("printed text gates on 'damage to a player'.");
    }

    [Fact]
    public void CombatDamageFromAnotherCreature_DoesNotTrigger()
    {
        var slasher = UnstoppableSlasherFactory.Create(_alice);
        slasher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(slasher);
        var other = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };

        var trigger = CombatTrigger(slasher);

        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, amount: 2))
            .Should().BeFalse("the trigger is gated on THIS creature dealing the damage.");
    }

    // -----------------------------------------------------------------------
    // Dies trigger — return tapped with two stun counters
    // -----------------------------------------------------------------------

    private Creature MakeBattlefieldSlasher(TriggerManager triggers)
    {
        var slasher = UnstoppableSlasherFactory.Create(_alice);
        slasher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(slasher);
        triggers.BindCard(slasher);
        return slasher;
    }

    [Fact]
    public void Dies_WithNoCounters_ReturnsTapped_WithTwoStunCounters_UnderOwnersControl()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var slasher = MakeBattlefieldSlasher(triggers);

        // Die.
        zones.MoveCardTo(slasher, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "dies trigger queues with no counters on it.");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        slasher.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(slasher,
            "CR 110.2 — returns under its OWNER's control.");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(slasher);
        slasher.Controller.Should().BeSameAs(_alice);
        slasher.IsTapped.Should().BeTrue("it returns TAPPED.");
        slasher.Counters.Count(CounterType.Stun).Should().Be(2,
            "it returns with two stun counters on it.");
    }

    [Fact]
    public void Dies_WithACounter_StaysInGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var slasher = MakeBattlefieldSlasher(triggers);

        // ANY counter suppresses the return — use a -1/-1 counter to prove the
        // check is over the whole bag, not +1/+1-only (broader than Undying).
        slasher.Counters.Add(CounterType.MinusOneMinusOne, 1);

        zones.MoveCardTo(slasher, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(0,
            "intervening-if fails when ANY counter was on it at death.");
        slasher.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Dies_AfterReturn_SecondDeathDoesNotTrigger_DueToStunCounters()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var slasher = MakeBattlefieldSlasher(triggers);

        // First death — returns with two stun counters.
        zones.MoveCardTo(slasher, ZoneType.Graveyard);
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        slasher.Counters.Count(CounterType.Stun).Should().Be(2);

        triggers.BindCard(slasher);

        // Second death — it now has stun counters, so it does NOT return.
        zones.MoveCardTo(slasher, ZoneType.Graveyard);
        triggers.PutPendingTriggersOnStack(_alice);

        stack.IsEmpty.Should().BeTrue(
            "the stun counters from the first return suppress the second dies trigger.");
        slasher.Zone.Should().Be(ZoneType.Graveyard);
    }
}
