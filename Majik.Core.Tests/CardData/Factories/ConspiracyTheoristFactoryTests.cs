using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ConspiracyTheoristFactory"/> (Streets of New
/// Capenna, {1}{R}, Creature — Human Shaman 2/2).
///
/// Oracle text (Scryfall-verified):
///   "Whenever this creature attacks, you may pay {1} and discard a card.
///    If you do, draw a card.
///    Whenever you discard one or more nonland cards, you may exile one of
///    them from your graveyard. If you do, you may cast it this turn."
///
/// Covers ONLY the card's unique behaviour (plus one identity assert):
/// - Identity (name, type, cost, P/T, Human + Shaman subtypes).
/// - Two triggered abilities attached, both gated to the battlefield.
/// - Attacks trigger fires only on THIS creature's attack and loots
///   (discard one + draw one) on accept; clean no-op on decline.
/// - Discard payoff fires on a nonland discard, exiles the discarded card
///   from the graveyard, and grants a may-cast-this-turn exile-cast.
/// - Discard payoff does NOT fire on a land discard / an opponent's discard.
/// - The loot's discard feeds the payoff (integration), and the
///   may-cast grant clears at the next Cleanup step ("this turn").
/// </summary>
[Trait("Color", "R")]
public class ConspiracyTheoristFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void StampSeed(Player p)
    {
        // Library content so a draw has something to find.
        for (var i = 0; i < 5; i++)
        {
            var c = new Instant($"Spell {i}", "{R}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    [Fact]
    public void Identity_NameTypeCostPTSubtypes()
    {
        var card = ConspiracyTheoristFactory.Create(_alice);

        card.Name.Should().Be("Conspiracy Theorist");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TwoTriggeredAbilities_BothGatedToBattlefield()
    {
        var card = ConspiracyTheoristFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "Conspiracy Theorist prints two triggered abilities — the attack loot + the discard payoff");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield),
            "CR 113.6 — abilities on a creature card function only from the battlefield");
    }

    // -----------------------------------------------------------------------
    // Attacks loot trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void AttacksTrigger_FiresOnlyForThisCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ct = ConspiracyTheoristFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ct);
        ct.SetZone(ZoneType.Battlefield);

        var other = new Creature("Bear", "{1}{G}", 2, 2);
        other.SetOwner(_alice);

        bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(other, _bob));
        triggers.PendingCount.Should().Be(0, "a different attacker does not fire Conspiracy Theorist's attack trigger");

        bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(ct, _bob));
        triggers.PendingCount.Should().Be(1, "Conspiracy Theorist's own attack fires its loot trigger");
    }

    [Fact]
    public void AttacksLoot_Accept_DiscardsOneAndDrawsOne()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        StampSeed(_alice);
        var hand = new Instant("Hand Card", "{R}");
        hand.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(hand);
        hand.SetZone(ZoneType.Hand);

        var ct = ConspiracyTheoristFactory.Create(
            _alice, bus, triggers, payAndDiscard: () => true);
        _alice.Zones.Battlefield.AddCard(ct);
        ct.SetZone(ZoneType.Battlefield);

        var libBefore = _alice.Zones.Library.Count;

        bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(ct, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Discarded the hand card (Hand → Graveyard), drew a replacement.
        hand.Zone.Should().Be(ZoneType.Graveyard, "the loot discards the hand card");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "discarded one, drew one — net hand size unchanged");
        _alice.Zones.Library.Count.Should().Be(libBefore - 1, "drew exactly one card");
    }

    [Fact]
    public void AttacksLoot_Decline_NoDiscardNoDraw()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        StampSeed(_alice);
        var hand = new Instant("Hand Card", "{R}");
        hand.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(hand);
        hand.SetZone(ZoneType.Hand);

        var ct = ConspiracyTheoristFactory.Create(
            _alice, bus, triggers, payAndDiscard: () => false);
        _alice.Zones.Battlefield.AddCard(ct);
        ct.SetZone(ZoneType.Battlefield);

        var libBefore = _alice.Zones.Library.Count;

        bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(ct, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        hand.Zone.Should().Be(ZoneType.Hand, "declining the optional cost discards nothing");
        _alice.Zones.Library.Count.Should().Be(libBefore, "no draw on decline");
    }

    // -----------------------------------------------------------------------
    // Discard payoff trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscardPayoff_Nonland_ExilesFromGraveyardAndGrantsMayCast()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ct = ConspiracyTheoristFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ct);
        ct.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        // The card is already in the graveyard (the discard completed); the
        // DiscardedEvent surface fires post-move (CR 701.8 documented posture).
        bus.Publish(new DiscardedEvent(_alice, bolt, wasCost: false));

        triggers.PendingCount.Should().Be(1, "Alice's nonland discard fires the payoff trigger");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bolt.Zone.Should().Be(ZoneType.Exile, "the discarded card is exiled from the graveyard");
        _alice.Zones.Exile.GetCards().Should().Contain(bolt);
        bolt.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice, "you may cast it this turn");
        bolt.RuntimeExileCastCost!.ToString().Should().Be(bolt.ManaCostValue.ToString());
    }

    [Fact]
    public void DiscardPayoff_Land_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ct = ConspiracyTheoristFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ct);
        ct.SetZone(ZoneType.Battlefield);

        var mountain = new Land("Mountain");
        mountain.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(mountain);
        mountain.SetZone(ZoneType.Graveyard);

        bus.Publish(new DiscardedEvent(_alice, mountain, wasCost: false));

        triggers.PendingCount.Should().Be(0,
            "land discards do not satisfy the printed 'nonland card' gate (CR 305.7)");
        mountain.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DiscardPayoff_OpponentsDiscard_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ct = ConspiracyTheoristFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(ct);
        ct.SetZone(ZoneType.Battlefield);

        var bobBolt = new Instant("Lightning Bolt", "{R}");
        bobBolt.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bobBolt);
        bobBolt.SetZone(ZoneType.Graveyard);

        bus.Publish(new DiscardedEvent(_bob, bobBolt, wasCost: false));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU discard' is scoped to Conspiracy Theorist's controller (CR 109.5)");
    }

    // -----------------------------------------------------------------------
    // Integration: the loot discard feeds the payoff; grant clears this turn
    // -----------------------------------------------------------------------

    [Fact]
    public void LootDiscard_FeedsPayoff_AndGrantClearsAtCleanup()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        // Wire the discard chokepoint to this player's bus so Fx.DiscardCard
        // publishes the DiscardedEvent the payoff observes.
        EventBusRegistry.Set(_alice, bus);
        try
        {
            StampSeed(_alice);
            var hand = new Instant("Hand Card", "{R}");
            hand.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(hand);
            hand.SetZone(ZoneType.Hand);

            var ct = ConspiracyTheoristFactory.Create(
                _alice, bus, triggers, payAndDiscard: () => true);
            _alice.Zones.Battlefield.AddCard(ct);
            ct.SetZone(ZoneType.Battlefield);

            // Attack — resolves the loot, which discards 'hand' via Fx.DiscardCard.
            bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(ct, _bob));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            // The discard published a DiscardedEvent → the payoff trigger queued.
            triggers.PendingCount.Should().Be(1, "the loot discard feeds the discard-payoff trigger");
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            hand.Zone.Should().Be(ZoneType.Exile, "the payoff exiled the looted card from the graveyard");
            hand.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice, "you may cast it this turn");

            // "This turn" — clears on the next Cleanup step (CR 514.2).
            bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
            hand.RuntimeExileCastAllowedCaster.Should().BeNull("the 'this turn' window ended at Cleanup");
            hand.RuntimeExileCastCost.Should().BeNull();
        }
        finally
        {
            EventBusRegistry.Remove(_alice);
        }
    }
}
