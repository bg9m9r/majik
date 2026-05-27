using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players;

/// <summary>
/// Tests for the Ring subsystem (CR 701.54 — "the Ring tempts you").
///
/// Coverage:
/// - First tempt creates the emblem (Player.Ring) and designates a chosen
///   Ring-bearer (CR 701.54a/c).
/// - Tempting again increments the count; designation moves to a new
///   creature (CR 701.54a/b).
/// - "is your Ring-bearer" reflects battlefield + control (CR 701.54e).
/// - Staged abilities turn on at thresholds 2 / 3 / 4 (CR 701.54c):
///     2+ → Ring-bearer attacks ⇒ draw a card, then discard a card.
///     3+ → Ring-bearer becomes blocked ⇒ blocker sacrificed at end of combat.
///     4+ → Ring-bearer deals combat damage to a player ⇒ each opponent
///          loses 3 life.
/// </summary>
public class RingStateTests
{
    private static Creature MakeCreature(Player owner, string name, int power = 2, int toughness = 2)
    {
        var c = new Creature(name, "{1}", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Tempting / designation
    // -----------------------------------------------------------------------

    [Fact]
    public void FirstTempt_CreatesRing_AndDesignatesChosenBearer()
    {
        var alice = new Player("Alice", 20);
        var bear = MakeCreature(alice, "Grizzly Bears");

        alice.Ring.Should().BeNull("no Ring exists before the first tempt");

        alice.TheRingTemptsYou(bear);

        alice.Ring.Should().NotBeNull("the emblem named The Ring is created on first tempt (CR 701.54c)");
        alice.Ring!.TemptCount.Should().Be(1);
        alice.Ring.RingBearer.Should().BeSameAs(bear);
        alice.Ring.IsRingBearer(bear).Should().BeTrue();
        alice.Ring.RingBearerIsLegendary.Should().BeTrue("Ring-bearer is legendary (CR 701.54c)");
    }

    [Fact]
    public void Tempt_Twice_IncrementsCount()
    {
        var alice = new Player("Alice", 20);
        var bear = MakeCreature(alice, "Grizzly Bears");

        alice.TheRingTemptsYou(bear);
        alice.TheRingTemptsYou(bear);

        alice.Ring!.TemptCount.Should().Be(2);
    }

    [Fact]
    public void DesignatingNewBearer_MovesDesignation()
    {
        var alice = new Player("Alice", 20);
        var first = MakeCreature(alice, "First");
        var second = MakeCreature(alice, "Second");

        alice.TheRingTemptsYou(first);
        alice.Ring!.IsRingBearer(first).Should().BeTrue();

        alice.TheRingTemptsYou(second);
        alice.Ring!.RingBearer.Should().BeSameAs(second);
        alice.Ring.IsRingBearer(second).Should().BeTrue();
        alice.Ring.IsRingBearer(first).Should().BeFalse("designation is unique — it moved (CR 701.54b)");
    }

    [Fact]
    public void IsRingBearer_FalseWhenCreatureLeavesBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bear = MakeCreature(alice, "Grizzly Bears");
        alice.TheRingTemptsYou(bear);

        bear.SetZone(ZoneType.Graveyard);

        alice.Ring!.IsRingBearer(bear).Should().BeFalse(
            "CR 701.54e — 'is your Ring-bearer' requires it be on the battlefield under your control");
    }

    // -----------------------------------------------------------------------
    // Always-on: can't be blocked by creatures with greater power
    // -----------------------------------------------------------------------

    [Fact]
    public void RingBearer_CannotBeBlockedByGreaterPowerCreature()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bearer = MakeCreature(alice, "Bearer", power: 2, toughness: 2);
        alice.TheRingTemptsYou(bearer);

        var smallBlocker = MakeCreature(bob, "Small", power: 1, toughness: 1);
        var bigBlocker = MakeCreature(bob, "Big", power: 5, toughness: 5);

        BlockLegality.CanBlock(smallBlocker, bearer, out _).Should().BeTrue(
            "power 1 ≤ Ring-bearer power 2 — legal block");
        BlockLegality.CanBlock(bigBlocker, bearer, out _).Should().BeFalse(
            "power 5 > Ring-bearer power 2 — can't block (CR 701.54c)");
    }

    // -----------------------------------------------------------------------
    // 2+ — Ring-bearer attacks ⇒ draw a card, then discard a card
    // -----------------------------------------------------------------------

    [Fact]
    public void TwoPlusTempts_RingBearerAttacks_DrawsThenDiscards()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bearer = MakeCreature(alice, "Bearer");

        // Two cards in library, one already in hand to be discarded.
        var lib1 = new Card("Lib1", "{1}"); lib1.SetOwner(alice);
        var inHand = new Card("InHand", "{1}"); inHand.SetOwner(alice);
        alice.Zones.Library.AddCard(lib1);
        alice.Zones.Hand.AddCard(inHand);

        // Tempt twice — staged 2+ ability is now live.
        alice.TheRingTemptsYou(bearer, bus, triggers, () => new[] { alice, bob });
        alice.TheRingTemptsYou(bearer, bus, triggers, () => new[] { alice, bob });
        alice.Ring!.TemptCount.Should().Be(2);

        var handBefore = alice.Zones.Hand.GetCards().Count();

        bus.Publish(new CreatureAttacksEvent(bearer, bob));
        triggers.PendingCount.Should().Be(1, "2+ tempts — Ring-bearer attack trigger fires");
        triggers.PutPendingTriggersOnStack(alice);
        stack.Pop()!.Resolve();

        // Loot is net-neutral on hand size (draw 1, discard 1).
        alice.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "drew a card, then discarded a card");
        alice.Zones.Graveyard.GetCards().Should().ContainSingle(c => c.Name == "InHand");
    }

    [Fact]
    public void OneTempt_RingBearerAttacks_DoesNotLoot()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var alice = new Player("Alice", 20);
        var bearer = MakeCreature(alice, "Bearer");

        alice.TheRingTemptsYou(bearer, bus, triggers, () => new[] { alice });
        alice.Ring!.TemptCount.Should().Be(1);

        bus.Publish(new CreatureAttacksEvent(bearer, alice));
        triggers.PendingCount.Should().Be(0,
            "only tempted once — the 2+ loot ability is not yet live");
    }

    // -----------------------------------------------------------------------
    // 3+ — Ring-bearer becomes blocked ⇒ blocker sacrificed at end of combat
    // -----------------------------------------------------------------------

    [Fact]
    public void ThreePlusTempts_RingBearerBlocked_BlockerSacrificedAtEndOfCombat()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bearer = MakeCreature(alice, "Bearer", power: 4, toughness: 4);
        var blocker = MakeCreature(bob, "Blocker", power: 1, toughness: 4);

        alice.TheRingTemptsYou(bearer, bus, triggers, () => new[] { alice, bob });
        alice.TheRingTemptsYou(bearer, bus, triggers, () => new[] { alice, bob });
        alice.TheRingTemptsYou(bearer, bus, triggers, () => new[] { alice, bob });
        alice.Ring!.TemptCount.Should().Be(3);

        // Build a combat with bearer attacking, blocker blocking it.
        var combat = new Majik.Core.Combat.Combat(alice, bob);
        var attacker = new Attacker(bearer, bob, null);
        combat.AddAttacker(attacker);
        attacker.AddBlocker(new Blocker(blocker, attacker, false, false, false));

        bus.Publish(new BlockersDeclaredEvent(combat));
        triggers.PendingCount.Should().Be(1, "3+ tempts — Ring-bearer-blocked trigger fires");
        triggers.PutPendingTriggersOnStack(alice);
        stack.Pop()!.Resolve();

        // Not sacrificed yet — only at end of combat.
        blocker.Zone.Should().Be(ZoneType.Battlefield, "the sacrifice is delayed to end of combat");

        bus.Publish(new CombatEndedEvent(combat));
        blocker.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.54c — the blocking creature's controller sacrifices it at end of combat");
        bob.Zones.Graveyard.GetCards().Should().Contain(c => c.Name == "Blocker");
    }

    // -----------------------------------------------------------------------
    // 4+ — Ring-bearer deals combat damage to a player ⇒ each opponent loses 3
    // -----------------------------------------------------------------------

    [Fact]
    public void FourPlusTempts_RingBearerDealsCombatDamageToPlayer_EachOpponentLosesThree()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bearer = MakeCreature(alice, "Bearer", power: 2, toughness: 2);

        for (int i = 0; i < 4; i++)
            alice.TheRingTemptsYou(bearer, bus, triggers, () => new[] { alice, bob });
        alice.Ring!.TemptCount.Should().Be(4);

        var bobBefore = bob.LifeTotal;
        var aliceBefore = alice.LifeTotal;

        bus.Publish(new CombatDamageDealtEvent(bearer, bob, 2));
        triggers.PendingCount.Should().Be(1, "4+ tempts — combat-damage trigger fires");
        triggers.PutPendingTriggersOnStack(alice);
        stack.Pop()!.Resolve();

        bob.LifeTotal.Should().Be(bobBefore - 3, "each opponent loses 3 life (CR 701.54c)");
        alice.LifeTotal.Should().Be(aliceBefore, "the Ring's controller is not an opponent of themselves");
    }

    [Fact]
    public void ThreeTempts_RingBearerDealsCombatDamage_DoesNotDrainLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bearer = MakeCreature(alice, "Bearer");

        for (int i = 0; i < 3; i++)
            alice.TheRingTemptsYou(bearer, bus, triggers, () => new[] { alice, bob });

        bus.Publish(new CombatDamageDealtEvent(bearer, bob, 2));
        triggers.PendingCount.Should().Be(0, "only tempted 3 times — the 4+ ability is not live");
    }
}
