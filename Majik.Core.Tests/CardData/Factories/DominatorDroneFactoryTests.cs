using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Dominator Drone (Battle for Zendikar, {2}{B}).
///
/// Creature — Eldrazi Drone 3/2 (colorless — Devoid). Oracle text (verified
/// against Scryfall 2026-06-02):
///   "Devoid (This card has no color.)
///    Ingest (Whenever this creature deals combat damage to a player, that
///    player exiles the top card of their library.)
///    When this creature enters, if you control another colorless creature,
///    each opponent loses 2 life."
///
/// Covers:
///   - Card shape: name, Creature, Eldrazi + Drone subtypes, {2}{B}, 3/2.
///   - Devoid: colorless despite the {B} pip (CardColors.GetColors empty).
///   - Ingest combat trigger: damaging a player exiles the top of THEIR
///     library; damaging a creature does not fire; empty library is a no-op.
///   - ETB intervening-if drain (CR 603.4): "each opponent loses 2 life" only
///     when the controller controls ANOTHER colorless creature; the condition
///     gate is re-checked, the drain hits each opponent.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "C")]
public class DominatorDroneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DominatorDrone_IsEldraziDrone_3_2_AtCost2B()
    {
        var c = DominatorDroneFactory.Create(_alice);

        c.Name.Should().Be("Dominator Drone");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DominatorDrone_IsColorless_ViaDevoid()
    {
        var c = DominatorDroneFactory.Create(_alice);

        // CR 702.114 — Devoid: colorless despite the {B} pip.
        CardColors.GetColors(c).Should().BeEmpty(
            "Devoid makes Dominator Drone colorless regardless of the {B} pip");
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Devoid");
    }

    [Fact]
    public void DominatorDrone_HasIngestKeywordMarker()
    {
        var c = DominatorDroneFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Ingest");
    }

    [Fact]
    public void DominatorDrone_HasIngestTrigger_AndEtbTrigger()
    {
        var c = DominatorDroneFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2, "Ingest combat trigger + ETB drain trigger");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield));
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DominatorDrone()
    {
        var card = NamedCardFactory.Create("Dominator Drone", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Dominator Drone");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(2);
    }

    // ─── Ingest (CR 701.34) ─────────────────────────────────────────────────

    [Fact]
    public void Ingest_CombatDamageToPlayer_ExilesTopOfTheirLibrary()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var topCard = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var drone = DominatorDroneFactory.Create(_alice, triggers, opponentResolver: null);
        _alice.Zones.Battlefield.AddCard(drone);
        drone.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(drone, _bob, 3));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(topCard,
            "the damaged player exiles the top card of THEIR library (Ingest)");
        _bob.Zones.Library.GetCards().Should().NotContain(topCard);
        topCard.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Ingest_CombatDamageToCreature_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var topCard = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var blocker = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };
        _bob.Zones.Battlefield.AddCard(blocker);
        blocker.SetZone(ZoneType.Battlefield);

        var drone = DominatorDroneFactory.Create(_alice, triggers, opponentResolver: null);
        _alice.Zones.Battlefield.AddCard(drone);
        drone.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(drone, blocker, 3));
        triggers.PutPendingTriggersOnStack(_alice);

        stack.IsEmpty.Should().BeTrue("Ingest only fires on damage to a player");
        _bob.Zones.Library.GetCards().Should().Contain(topCard);
    }

    [Fact]
    public void Ingest_EmptyLibrary_IsNoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var drone = DominatorDroneFactory.Create(_alice, triggers, opponentResolver: null);
        _alice.Zones.Battlefield.AddCard(drone);
        drone.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(drone, _bob, 3));
        triggers.PutPendingTriggersOnStack(_alice);
        var act = () => stack.Pop()!.Resolve();

        act.Should().NotThrow("exiling from an empty library is simply a no-op");
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // ─── ETB intervening-if drain (CR 603.4 / 119.3) ────────────────────────

    [Fact]
    public void Etb_FiresForSelfEntering_NotOtherCard()
    {
        var c = DominatorDroneFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        var selfEvt = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(selfEvt, etb).Should().BeTrue(
            "the ETB trigger fires when Dominator Drone itself enters");

        var other = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var otherEvt = new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(otherEvt, etb).Should().BeFalse(
            "the ETB trigger fires only for this specific card");
    }

    [Fact]
    public void Etb_InterveningIf_FalseWhenNoOtherColorlessCreature()
    {
        // CR 603.4 — only another COLORLESS creature satisfies the condition.
        var drone = DominatorDroneFactory.Create(_alice);
        drone.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(drone);

        var etb = drone.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        // Only the Drone itself on the battlefield — "another" excludes it,
        // so the intervening-if is false.
        etb.InterveningIf!().Should().BeFalse(
            "no OTHER colorless creature ⇒ intervening-if condition is unmet");
    }

    [Fact]
    public void Etb_InterveningIf_FalseWhenOtherCreatureIsColored()
    {
        var drone = DominatorDroneFactory.Create(_alice);
        drone.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(drone);

        // A coloured creature does not satisfy "another colorless creature".
        var greenBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        greenBear.SetOwner(_alice);
        greenBear.SetController(_alice);
        greenBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(greenBear);

        var etb = drone.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        etb.InterveningIf!().Should().BeFalse(
            "a coloured creature does not count as 'another colorless creature'");
    }

    [Fact]
    public void Etb_InterveningIf_TrueWithAnotherColorlessCreature()
    {
        var drone = DominatorDroneFactory.Create(_alice);
        drone.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(drone);

        // A colorless creature (no pips → empty color set) Alice controls.
        var hedron = new Creature("Hedron Crawler", "{3}", 1, 1);
        hedron.SetOwner(_alice);
        hedron.SetController(_alice);
        hedron.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hedron);

        var etb = drone.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        etb.InterveningIf!().Should().BeTrue(
            "controlling another colorless creature satisfies the intervening-if");
    }

    [Fact]
    public void Etb_InterveningIf_IgnoresOpponentColorlessCreatures()
    {
        var drone = DominatorDroneFactory.Create(_alice);
        drone.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(drone);

        // A colorless creature controlled by the OPPONENT — does not count
        // ("you control").
        var bobDrone = new Creature("Bob's Drone", "{2}", 1, 1);
        bobDrone.SetOwner(_bob);
        bobDrone.SetController(_bob);
        bobDrone.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobDrone);

        var etb = drone.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        etb.InterveningIf!().Should().BeFalse(
            "'you control' — an opponent's colorless creature does not count");
    }

    [Fact]
    public void Etb_Drain_EachOpponentLosesTwoLife()
    {
        var drone = DominatorDroneFactory.Create(
            _alice,
            triggers: null,
            opponentResolver: () => new[] { _bob });

        var etb = drone.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        // Simulate the drain resolving.
        foreach (var e in etb.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(18,
            "Dominator Drone's ETB drains 2 life from each opponent");
    }

    [Fact]
    public void Etb_Drain_NoResolver_NoOps()
    {
        var drone = DominatorDroneFactory.Create(_alice);

        var etb = drone.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        foreach (var e in etb.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no opponent resolver ⇒ drain no-ops");
    }
}
