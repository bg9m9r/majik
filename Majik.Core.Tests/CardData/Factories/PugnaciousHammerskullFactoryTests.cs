using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Pugnacious Hammerskull (Lost Caverns of Ixalan, {2}{G},
/// Creature — Dinosaur 6/6).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Whenever this creature attacks while you don't control another Dinosaur,
///    put a stun counter on it. (If a permanent with a stun counter would
///    become untapped, remove one from it instead.)"
///
/// Covers the card's UNIQUE behaviour:
///   - Identity: {2}{G}, Dinosaur, 6/6.
///   - Attacking while controlling NO other Dinosaur → one stun counter (CR 122.1g).
///   - Attacking while controlling ANOTHER Dinosaur → no stun counter
///     (intervening-if "while you don't control another Dinosaur" fails, CR 603.4).
///   - A non-Dinosaur creature on the battlefield does NOT satisfy the gate.
///   - Attacking with a DIFFERENT creature (this one not attacking) → no trigger.
/// Dispatch / well-formedness are covered globally by CardFactoryContractTests.
/// </summary>
[Trait("Color", "G")]
public class PugnaciousHammerskullFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // --- Identity ----------------------------------------------------------

    [Fact]
    public void PugnaciousHammerskull_Identity_Dinosaur_6_6_At2G()
    {
        var card = PugnaciousHammerskullFactory.Create(_alice);

        card.Name.Should().Be("Pugnacious Hammerskull");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(6);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PugnaciousHammerskull_HasOneAttackTrigger()
    {
        var card = PugnaciousHammerskullFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // --- Attack trigger: stun-counter gated on no other Dinosaur -----------

    [Fact]
    public void Attacking_NoOtherDinosaur_PutsOneStunCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = PugnaciousHammerskullFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, card);

        bus.Publish(new AttackersDeclaredEvent(AttackWith(_alice, card)));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 122.1g — one stun counter placed on itself.
        card.Counters.Count(CounterType.Stun).Should().Be(1);
    }

    [Fact]
    public void Attacking_WhileControllingAnotherDinosaur_NoStunCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = PugnaciousHammerskullFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, card);

        // Another Dinosaur Alice controls — the intervening-if gate fails.
        var otherDino = new Creature("Other Dino", "G", 2, 2,
            subtypes: new[] { CardSubtype.Dinosaur }) { Owner = _alice };
        otherDino.SetController(_alice);
        PlaceOnBattlefield(_alice, otherDino);

        bus.Publish(new AttackersDeclaredEvent(AttackWith(_alice, card)));

        // CR 603.4 — intervening-if false at trigger time, no trigger queued.
        triggers.PendingCount.Should().Be(0);
        card.Counters.Count(CounterType.Stun).Should().Be(0);
    }

    [Fact]
    public void Attacking_WithNonDinosaurOnBattlefield_StillPutsStunCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = PugnaciousHammerskullFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, card);

        // A non-Dinosaur creature does NOT satisfy "another Dinosaur".
        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear }) { Owner = _alice };
        bear.SetController(_alice);
        PlaceOnBattlefield(_alice, bear);

        bus.Publish(new AttackersDeclaredEvent(AttackWith(_alice, card)));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        card.Counters.Count(CounterType.Stun).Should().Be(1);
    }

    [Fact]
    public void NotAttackingItself_NoTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = PugnaciousHammerskullFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, card);

        // A different creature attacks; Hammerskull stays home → no trigger.
        var other = new Creature("Runner", "G", 2, 2) { Owner = _alice };
        other.SetController(_alice);
        PlaceOnBattlefield(_alice, other);

        bus.Publish(new AttackersDeclaredEvent(AttackWith(_alice, other)));

        triggers.PendingCount.Should().Be(0);
        card.Counters.Count(CounterType.Stun).Should().Be(0);
    }

    // --- helpers -----------------------------------------------------------

    private static void PlaceOnBattlefield(Player player, Creature creature)
    {
        creature.SetZone(ZoneType.Battlefield);
        player.Zones.Battlefield.AddCard(creature);
    }

    private Majik.Core.Combat.Combat AttackWith(Player attacker, Creature creature)
    {
        var combat = new Majik.Core.Combat.Combat(attacker, _bob);
        combat.AddAttacker(new Attacker(creature, _bob));
        return combat;
    }
}
