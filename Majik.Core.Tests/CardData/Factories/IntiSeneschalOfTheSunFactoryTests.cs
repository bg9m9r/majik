using System;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Inti, Seneschal of the Sun (Outlaws of Thunder Junction,
/// {1}{R}, Legendary Creature — Human Knight 2/2). Oracle text (verified
/// against Scryfall):
///   "Whenever you attack, you may discard a card. When you do, put a +1/+1
///    counter on target attacking creature. It gains trample until end of
///    turn.
///    Whenever you discard one or more cards, exile the top card of your
///    library. You may play that card until your next end step."
///
/// Covers:
///   - Card identity (name, legendary, Human Knight, 2/2, {1}{R}, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Two triggered abilities (attack trigger + discard trigger).
///   - Attack trigger: on AttackersDeclaredEvent by the controller, "you may
///     discard a card"; when a card is discarded, a +1/+1 counter lands on a
///     target attacking creature and that creature gains Trample EOT.
///   - Attack trigger does NOT fire on an opponent's attack.
///   - Attack trigger "you may" opt-out: no discard → no counter / no trample.
///   - Discard trigger: a Hand→Graveyard move by the controller exiles the
///     top card of library and stamps the may-play-from-exile grant.
///   - Discard trigger does NOT fire on an opponent's discard.
/// </summary>
[Trait("Color", "R")]
public class IntiSeneschalOfTheSunFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInHand(Player owner, string name)
    {
        ICard c = new Card(name, "R");
        c.SetOwner(owner);
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return (Card)c;
    }

    private static Card NewCardInLibrary(Player owner, string name)
    {
        ICard c = new Card(name, "R");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return (Card)c;
    }

    private static Creature NewAttacker(Player controller, string name, int p = 1, int t = 1)
    {
        var creature = new Creature(name, "{R}", p, t);
        creature.SetOwner(controller);
        creature.SetController(controller);
        return creature;
    }

    private static Majik.Core.Combat.Combat AttackWith(
        Player attacker, Player defender, params Creature[] creatures)
    {
        var combat = new Majik.Core.Combat.Combat(attacker, defender);
        foreach (var c in creatures)
            combat.AddAttacker(new Attacker(c, defender));
        return combat;
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Inti_Identity_LegendaryHumanKnight_2_2_AtCost1R()
    {
        var card = IntiSeneschalOfTheSunFactory.Create(_alice);

        card.Name.Should().Be("Inti, Seneschal of the Sun");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Inti_HasTwoTriggeredAbilities_AttackAndDiscard()
    {
        var card = IntiSeneschalOfTheSunFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever you attack, you may discard a card. When you
    // do, put a +1/+1 counter on target attacking creature. It gains trample
    // until end of turn."
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_DiscardingACard_AddsCounterAndTrample_ToTargetAttacker()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var bear = NewAttacker(_alice, "Bear", 2, 2);
        bear.ActiveEffects = effects;

        // Target the attacking Bear; default "may" = discard.
        var card = IntiSeneschalOfTheSunFactory.Create(
            _alice, bus, triggers, effects,
            attackTargetResolver: combat => bear,
            mayDiscard: () => true);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var discardFodder = NewCardInHand(_alice, "Fodder");

        var combat = AttackWith(_alice, _bob, bear);
        bus.Publish(new AttackersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(1, "the attack trigger fires when you attack");

        var attack = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("on attack")));
        foreach (var e in attack.Effects) e.Execute();

        discardFodder.Zone.Should().Be(ZoneType.Graveyard, "the discard cost was paid");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a +1/+1 counter lands on the target attacking creature");
        bear.Power.Should().Be(3, "the +1/+1 counter buffs power via layer 7c");
        bear.Toughness.Should().Be(3, "the +1/+1 counter buffs toughness via layer 7c");
        effects.Compute(bear).Keywords.Should().Contain("Trample",
            "the target attacking creature gains trample until end of turn");
    }

    [Fact]
    public void AttackTrigger_OptOutOfDiscard_NoCounterNoTrample()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var bear = NewAttacker(_alice, "Bear", 2, 2);
        bear.ActiveEffects = effects;

        var card = IntiSeneschalOfTheSunFactory.Create(
            _alice, bus, triggers, effects,
            attackTargetResolver: combat => bear,
            mayDiscard: () => false); // decline the "you may discard"
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var inHand = NewCardInHand(_alice, "StaysInHand");

        var combat = AttackWith(_alice, _bob, bear);
        bus.Publish(new AttackersDeclaredEvent(combat));

        var attack = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("on attack")));
        foreach (var e in attack.Effects) e.Execute();

        inHand.Zone.Should().Be(ZoneType.Hand, "declined the optional discard");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no discard means the reflexive 'when you do' trigger does not happen");
        effects.Compute(bear).Keywords.Should().NotContain("Trample");
    }

    [Fact]
    public void AttackTrigger_OpponentAttacks_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = IntiSeneschalOfTheSunFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var bobBear = NewAttacker(_bob, "BobBear", 2, 2);
        var combat = AttackWith(_bob, _alice, bobBear);
        bus.Publish(new AttackersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(0,
            "'Whenever you attack' only fires when Inti's controller is the attacker");
    }

    // -----------------------------------------------------------------------
    // Discard trigger — "Whenever you discard one or more cards, exile the top
    // card of your library. You may play that card until your next end step."
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscardTrigger_ControllerDiscards_ExilesTop_AndGrantsPlay()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = IntiSeneschalOfTheSunFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var top = NewCardInLibrary(_alice, "TopCard");

        // A Hand → Graveyard move by Alice = a discard. The discarder is
        // read off the moved card's owner (same funnel as Containment
        // Construct — the engine has no dedicated discard event).
        var discarded = new Card("Discarded", "R");
        discarded.SetOwner(_alice);
        bus.Publish(new CardMovedEvent(discarded, ZoneType.Hand, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(1, "discarding fires Inti's second ability");

        var discardTrigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("on discard")));
        foreach (var e in discardTrigger.Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Exile, "the top card of library is exiled");
        _alice.Zones.Exile.GetCards().Should().Contain(top);
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "you may play that card until your next end step");
    }

    [Fact]
    public void DiscardTrigger_OpponentDiscards_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = IntiSeneschalOfTheSunFactory.Create(_alice, bus, triggers, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        NewCardInLibrary(_alice, "TopCard");

        var bobsCard = new Card("BobDiscard", "R");
        bobsCard.SetOwner(_bob);
        bus.Publish(new CardMovedEvent(bobsCard, ZoneType.Hand, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(0,
            "'Whenever you discard' only triggers off the controller's own discards");
    }
}
