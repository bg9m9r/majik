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
/// Tests for Robber of the Rich (Throne of Eldraine, {1}{R}).
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Human Archer Rogue).
///   - NamedCardFactory dispatch.
///   - Reach + Haste keyword markers.
///   - Attack trigger structure (active on battlefield).
///   - Intervening-if: trigger only fires / resolves when the defending
///     player has strictly more cards in hand than the Robber's controller.
///   - Mechanic: on a satisfied attack, the top card of the defending
///     player's library is exiled and granted to the Robber's controller
///     (and only them) via the runtime exile-cast grant.
///   - Empty-library edge: the exile step is a graceful no-op.
///   - Damage/attack against an equal-or-smaller hand does NOT fire.
///   - EOT cleanup clears the may-cast grant on the next Cleanup step.
/// </summary>
public class RobberOfTheRichTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature MakeCard(string name, string cost, Player owner)
    {
        var c = new Creature(name, cost, 1, 1) { Owner = owner };
        return c;
    }

    [Fact]
    public void Robber_Is_HumanArcherRogue_2_2_At1R()
    {
        var robber = RobberOfTheRichFactory.Create(_alice);

        robber.Name.Should().Be("Robber of the Rich");
        robber.ManaCost.Should().Be("{1}{R}");
        robber.HasType(CardType.Creature).Should().BeTrue();
        robber.HasSubtype(CardSubtype.Human).Should().BeTrue();
        robber.HasSubtype(CardSubtype.Archer).Should().BeTrue();
        robber.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        robber.BasePower.Should().Be(2);
        robber.BaseToughness.Should().Be(2);
        robber.Owner.Should().BeSameAs(_alice);
        robber.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Robber_HasReachAndHaste()
    {
        var robber = RobberOfTheRichFactory.Create(_alice);

        var keywords = robber.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Reach");
        keywords.Should().Contain("Haste");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Robber()
    {
        var card = NamedCardFactory.Create("Robber of the Rich", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Robber of the Rich");
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "attack trigger is wired");
    }

    [Fact]
    public void Robber_HasAttackTrigger_ActiveOnBattlefieldOnly()
    {
        var robber = RobberOfTheRichFactory.Create(_alice);

        var triggers = robber.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    [Fact]
    public void Robber_AttacksWithDefenderBiggerHand_ExilesTopOfDefenderLibrary()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Bob (defender) has 2 cards in hand; Alice (Robber's controller) has 0.
        _bob.Zones.Hand.AddCard(MakeCard("Forest", "", _bob));
        _bob.Zones.Hand.AddCard(MakeCard("Island", "", _bob));

        var topCard = MakeCard("Llanowar Elves", "G", _bob);
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var robber = RobberOfTheRichFactory.Create(_alice, zones, triggers, bus);
        _alice.Zones.Battlefield.AddCard(robber);
        robber.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(robber, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(topCard,
            "top of defending player's library is exiled");
        _bob.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void Robber_ExiledCard_IsCastable_ByRobberController_NotOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        _bob.Zones.Hand.AddCard(MakeCard("Forest", "", _bob));
        _bob.Zones.Hand.AddCard(MakeCard("Island", "", _bob));

        var pilfered = MakeCard("Llanowar Elves", "G", _bob);
        _bob.Zones.Library.AddCard(pilfered);
        pilfered.SetZone(ZoneType.Library);

        var robber = RobberOfTheRichFactory.Create(_alice, zones, triggers, bus);
        _alice.Zones.Battlefield.AddCard(robber);
        robber.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(robber, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        pilfered.Zone.Should().Be(ZoneType.Exile);
        pilfered.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Robber's controller is the named caster (not the card's owner)");
        pilfered.RuntimeExileCastCost.Should().NotBeNull();

        var altCost = new ExileCastAlternativeCost(
            "Robber: you may cast that card",
            pilfered.RuntimeExileCastCost!);

        altCost.CanCastFor(pilfered, _alice).Should().BeTrue(
            "the runtime grant nominates Alice as the allowed caster");
        altCost.CanCastFor(pilfered, _bob).Should().BeFalse(
            "the card's owner cannot use the grant");
    }

    [Fact]
    public void Robber_AttacksWithDefenderEqualHand_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Both players have 1 card in hand — intervening-if not satisfied.
        _bob.Zones.Hand.AddCard(MakeCard("Forest", "", _bob));
        _alice.Zones.Hand.AddCard(MakeCard("Mountain", "", _alice));

        var topCard = MakeCard("Llanowar Elves", "G", _bob);
        _bob.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var robber = RobberOfTheRichFactory.Create(_alice, zones, triggers, bus);
        _alice.Zones.Battlefield.AddCard(robber);
        robber.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(robber, _bob));
        triggers.PendingCount.Should().Be(0,
            "intervening-if fails when the defender does not have MORE cards than the Robber's controller (CR 603.4)");

        _bob.Zones.Exile.GetCards().Should().BeEmpty();
        _bob.Zones.Library.GetCards().Should().Contain(topCard);
    }

    [Fact]
    public void Robber_EmptyLibrary_NoExile_Graceful()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        _bob.Zones.Hand.AddCard(MakeCard("Forest", "", _bob));
        _bob.Zones.Library.GetCards().Should().BeEmpty();

        var robber = RobberOfTheRichFactory.Create(_alice, zones, triggers, bus);
        _alice.Zones.Battlefield.AddCard(robber);
        robber.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(robber, _bob));
        triggers.PutPendingTriggersOnStack(_alice);

        var trigger = stack.Pop();
        var act = () => trigger!.Resolve();
        act.Should().NotThrow(
            "the exile step is a no-op against an empty library");

        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Robber_EOTCleanup_ClearsExileCastGrant()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        _bob.Zones.Hand.AddCard(MakeCard("Forest", "", _bob));

        var pilfered = MakeCard("Llanowar Elves", "G", _bob);
        _bob.Zones.Library.AddCard(pilfered);
        pilfered.SetZone(ZoneType.Library);

        var robber = RobberOfTheRichFactory.Create(_alice, zones, triggers, bus);
        _alice.Zones.Battlefield.AddCard(robber);
        robber.SetZone(ZoneType.Battlefield);

        bus.Publish(new CreatureAttacksEvent(robber, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        pilfered.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        pilfered.RuntimeExileCastAllowedCaster.Should().BeNull(
            "EOT cleanup clears the may-cast grant");
        pilfered.RuntimeExileCastCost.Should().BeNull();
    }
}
