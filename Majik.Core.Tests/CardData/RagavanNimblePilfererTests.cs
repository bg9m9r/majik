using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Ragavan, Nimble Pilferer (Modern Horizons 2, {R}).
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Legendary, Monkey Pirate).
///   - NamedCardFactory dispatch.
///   - Combat-damage-to-a-player trigger structure (active on battlefield).
///   - Mechanic: damage to opponent creates a Treasure under the Ragavan
///     controller AND exiles the top card of the damaged player's library.
///   - Mechanic: the exiled card is castable by the Ragavan controller
///     under the runtime exile-cast grant
///     (<see cref="ExileCastAlternativeCost"/>), and only by them.
///   - Empty-library edge: the exile step is a no-op, the Treasure is
///     still created (printed text is "create a Treasure. Then exile …" —
///     two independent clauses; the no-op is on the exile side only).
///   - Damage to a creature (not a player) does NOT fire the trigger.
///   - EOT cleanup clears the may-cast grant on the next Cleanup step.
/// </summary>
public class RagavanNimblePilfererTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Ragavan_Is_LegendaryMonkeyPirate_2_1_AtCostR()
    {
        var rag = RagavanNimblePilfererFactory.Create(_alice);

        rag.Name.Should().Be("Ragavan, Nimble Pilferer");
        rag.ManaCost.Should().Be("{R}");
        rag.HasType(CardType.Creature).Should().BeTrue();
        rag.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        rag.HasSubtype(CardSubtype.Monkey).Should().BeTrue();
        rag.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        rag.BasePower.Should().Be(2);
        rag.BaseToughness.Should().Be(1);
        rag.Owner.Should().BeSameAs(_alice);
        rag.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Ragavan()
    {
        var card = NamedCardFactory.Create("Ragavan, Nimble Pilferer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ragavan, Nimble Pilferer");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Monkey).Should().BeTrue();
        card.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage-to-a-player trigger is wired");
    }

    [Fact]
    public void Ragavan_HasCombatDamageTrigger_ActiveOnBattlefieldOnly()
    {
        var rag = RagavanNimblePilfererFactory.Create(_alice);

        var triggers = rag.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    [Fact]
    public void Ragavan_CombatDamageToOpponent_CreatesTreasureAndExilesTop()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Bob's library top — Ragavan should exile this card.
        var topCard = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var rag = RagavanNimblePilfererFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rag);
        rag.SetZone(ZoneType.Battlefield);

        // Fire combat damage to Bob.
        bus.Publish(new CombatDamageDealtEvent(rag, _bob, 2));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Treasure token under Alice's control.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Should().Contain(c => c.Name == "Treasure",
                "Ragavan's controller gets a Treasure token");

        // Bob's library top exiled.
        _bob.Zones.Exile.GetCards().Should().Contain(topCard,
            "top of damaged player's library is exiled");
        _bob.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void Ragavan_ExiledCard_IsCastable_ByRagavanController_NotOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Bob's library top — once Ragavan hits, this is the exile-and-cast
        // candidate. Llanowar Elves (mana cost {G}) is a permanent card, so
        // the grant carries its printed cost.
        var pilfered = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(pilfered);
        pilfered.SetZone(ZoneType.Library);

        var rag = RagavanNimblePilfererFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rag);
        rag.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(rag, _bob, 2));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Card is now in exile with a runtime grant naming Alice.
        pilfered.Zone.Should().Be(ZoneType.Exile);
        pilfered.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Ragavan's controller is the named caster (not the card's owner)");
        pilfered.RuntimeExileCastCost.Should().NotBeNull();
        pilfered.RuntimeExileCastCost!.ToString().Should().Be(
            pilfered.ManaCostValue.ToString(),
            "grant cost equals the card's printed mana cost");

        // Build the matching alt cost; Alice may cast, Bob (the owner) may not.
        var altCost = new ExileCastAlternativeCost(
            "Ragavan: you may cast that card",
            pilfered.RuntimeExileCastCost!);

        altCost.CanCastFor(pilfered, _alice).Should().BeTrue(
            "the runtime grant nominates Alice as the allowed caster");
        altCost.CanCastFor(pilfered, _bob).Should().BeFalse(
            "Ragavan's permission is scoped to the Ragavan controller — " +
            "the card's owner cannot use the grant");
    }

    [Fact]
    public void Ragavan_CombatDamageToCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var topCard = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var rag = RagavanNimblePilfererFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rag);
        rag.SetZone(ZoneType.Battlefield);

        // A blocker takes the damage instead of a player.
        var blocker = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _bob, Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(blocker);
        blocker.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(rag, blocker, 2));
        triggers.PendingCount.Should().Be(0,
            "Ragavan only triggers on combat damage to a player (CR 510 / oracle)");

        // No Treasure, no exile.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
        _bob.Zones.Library.GetCards().Should().Contain(topCard);
    }

    [Fact]
    public void Ragavan_EmptyLibrary_NoExile_Graceful()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        _bob.Zones.Library.GetCards().Should().BeEmpty(
            "Bob starts with an empty library for this edge case");

        var rag = RagavanNimblePilfererFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rag);
        rag.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(rag, _bob, 2));
        triggers.PutPendingTriggersOnStack(_alice);

        var trigger = stack.Pop();
        var act = () => trigger!.Resolve();
        act.Should().NotThrow(
            "the exile step is a no-op against an empty library — empty-library " +
            "loss is the SBA's job, not Ragavan's trigger");

        // Treasure was still created (CR 603.1 — independent printed clauses).
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Should().Contain(c => c.Name == "Treasure",
                "the Treasure clause runs even when the library is empty");
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Ragavan_EOTCleanup_ClearsExileCastGrant()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var pilfered = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(pilfered);
        pilfered.SetZone(ZoneType.Library);

        var rag = RagavanNimblePilfererFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(rag);
        rag.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(rag, _bob, 2));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        pilfered.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // Cleanup step fires — CR 514.2 / 514.3.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        pilfered.RuntimeExileCastAllowedCaster.Should().BeNull(
            "EOT cleanup clears the may-cast grant");
        pilfered.RuntimeExileCastCost.Should().BeNull();
    }
}
