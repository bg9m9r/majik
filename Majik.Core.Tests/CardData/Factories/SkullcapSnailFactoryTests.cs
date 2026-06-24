using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Skullcap Snail — Creature — Fungus Snail {1}{B}, 1/1.
///
/// Oracle text (Scryfall verified 2026-06):
///   "When this creature enters, target opponent exiles a card from their
///    hand."
///
/// The single rider is built on existing engine primitives:
///   * ETB trigger (CR 603.1 / 603.6a) — OnEnterBattlefieldSelf with a 1..1
///     "target opponent" request; on resolution the chosen opponent exiles a
///     card of their own choice (CR 609.2 / 102.1) from hand → exile
///     (CR 406.3 / 701.10a). Deterministic first-card fallback when no agent
///     is supplied (same opponent-chooses shape as Archon of Cruelty's
///     discard step).
/// </summary>
[Trait("Color", "B")]
public class SkullcapSnailFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SkullcapSnail_IsFungusSnailCreature_AtCost1B_1_1()
    {
        var snail = SkullcapSnailFactory.Create(_alice);

        snail.Name.Should().Be("Skullcap Snail");
        snail.HasType(CardType.Creature).Should().BeTrue();
        snail.HasSubtype(CardSubtype.Fungus).Should().BeTrue();
        snail.HasSubtype(CardSubtype.Snail).Should().BeTrue();
        snail.ManaCost.Should().Be("{1}{B}");
        snail.Power.Should().Be(1);
        snail.Toughness.Should().Be(1);
        snail.Owner.Should().BeSameAs(_alice);
        snail.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape — "target opponent exiles a card from their hand."
    // CR 603.1 / 603.6a.
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_IsBattlefieldActive_WithTargetOpponentRequest()
    {
        var snail = SkullcapSnailFactory.Create(_alice);

        var etbTrigger = snail.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Battlefield));

        etbTrigger.TargetRequests.Should().ContainSingle();
        etbTrigger.TargetRequests[0].Description.Should().Be("target opponent");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — the chosen opponent exiles a card from their hand.
    // CR 701.10a / 406.3 / 609.2.
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_Fires_TargetOpponentExilesACardFromHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var snail = SkullcapSnailFactory.Create(_alice, triggers);
        snail.SetZone(ZoneType.Battlefield);

        // Bob (the opponent) holds three cards.
        for (var i = 0; i < 3; i++)
        {
            var c = new Creature($"Bear {i}", "{1}{G}", 2, 2);
            c.SetOwner(_bob);
            c.SetZone(ZoneType.Hand);
            _bob.Zones.Hand.AddCard(c);
        }

        bus.Publish(new CardMovedEvent(snail, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "the creature entering triggers its ETB ability");

        var etbTrigger = snail.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Hand.GetCards().Should().HaveCount(2,
            "the opponent exiles exactly one card from their hand");
        _bob.Zones.Exile.GetCards().Should().HaveCount(1,
            "the chosen card goes to exile, not the graveyard (CR 406.3)");
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void EtbTrigger_EmptyHand_IsNoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var snail = SkullcapSnailFactory.Create(_alice, triggers);
        snail.SetZone(ZoneType.Battlefield);
        // Bob holds no cards.

        bus.Publish(new CardMovedEvent(snail, ZoneType.Hand, ZoneType.Battlefield));

        var etbTrigger = snail.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Exile.GetCards().Should().BeEmpty(
            "an empty hand exiles nothing (CR 701.10a)");
    }
}
