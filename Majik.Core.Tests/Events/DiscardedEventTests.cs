using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game.Phases;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// CR 701.8 — tests for <see cref="DiscardedEvent"/> publication across the
/// three real discard routes that all funnel through the central chokepoint
/// <see cref="Fx.DiscardCard"/>:
///   1. effect discards (<see cref="Fx.Discard(Player, int)"/>),
///   2. cost discards (the discard-cost surface in <c>Majik.Core/Costs/</c>),
///   3. the cleanup-step max-hand-size trim (CR 514.1).
/// Plus the <see cref="Triggers.OnDiscard(Player)"/> "you discard" gate.
/// </summary>
public class DiscardedEventTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInHand(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    // -----------------------------------------------------------------------
    // Route 1 — effect discard (Fx.Discard)
    // -----------------------------------------------------------------------

    [Fact]
    public void FxDiscard_PublishesDiscardedEvent_OnRegisteredBus_NotACost()
    {
        var bus = new EventBus();
        var captured = new List<DiscardedEvent>();
        bus.Subscribe<DiscardedEvent>(captured.Add);

        EventBusRegistry.Clear();
        EventBusRegistry.Set(_alice, bus);
        try
        {
            var card = NewCardInHand(_alice, "Effect Discarded");
            var discarded = Fx.Discard(_alice, 1);

            discarded.Should().ContainSingle().Which.Should().BeSameAs(card);
            captured.Should().ContainSingle();
            captured[0].Player.Should().BeSameAs(_alice);
            captured[0].Card.Should().BeSameAs(card);
            captured[0].WasCost.Should().BeFalse("an effect discard is not a cost");
            card.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            EventBusRegistry.Clear();
        }
    }

    [Fact]
    public void FxDiscard_NoBusRegistered_DoesNotThrow_StillMoves()
    {
        EventBusRegistry.Clear();
        var card = NewCardInHand(_alice, "X");

        var act = () => Fx.Discard(_alice, 1);
        act.Should().NotThrow();
        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Route 2 — cost discard (DiscardACardCost / DiscardSelfCost)
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscardACardCost_PublishesDiscardedEvent_AsACost()
    {
        var bus = new EventBus();
        DiscardedEvent? captured = null;
        bus.Subscribe<DiscardedEvent>(e => captured = e);

        EventBusRegistry.Clear();
        EventBusRegistry.Set(_alice, bus);
        try
        {
            var card = NewCardInHand(_alice, "Cost Discarded");
            var cost = new DiscardACardCost { Target = card };
            cost.Pay(_alice);

            captured.Should().NotBeNull();
            captured!.Player.Should().BeSameAs(_alice);
            captured.Card.Should().BeSameAs(card);
            captured.WasCost.Should().BeTrue("a discard cost IS a cost");
            card.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            EventBusRegistry.Clear();
        }
    }

    [Fact]
    public void DiscardSelfCost_PublishesDiscardedEvent_AsACost()
    {
        var bus = new EventBus();
        DiscardedEvent? captured = null;
        bus.Subscribe<DiscardedEvent>(e => captured = e);

        EventBusRegistry.Clear();
        EventBusRegistry.Set(_alice, bus);
        try
        {
            var card = NewCardInHand(_alice, "Self");
            var cost = new DiscardSelfCost(card);
            cost.Pay(_alice);

            captured.Should().NotBeNull();
            captured!.Card.Should().BeSameAs(card);
            captured.WasCost.Should().BeTrue();
        }
        finally
        {
            EventBusRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Route 3 — cleanup-step max-hand-size trim (CR 514.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void CleanupStep_DiscardToHandSize_PublishesDiscardedEvent_PerCard()
    {
        var bus = new EventBus();
        var captured = new List<DiscardedEvent>();
        bus.Subscribe<DiscardedEvent>(captured.Add);

        var zoneService = new Majik.Core.Services.ZoneService();
        // 9 cards in hand, max 7 → 2 discards.
        for (var i = 0; i < 9; i++) NewCardInHand(_alice, $"H{i}");

        var cleanup = new CleanupStep(bus, zoneService);
        cleanup.DiscardToHandSize(_alice, maxHandSize: 7);

        captured.Should().HaveCount(2, "two cards over the 7-card max");
        captured.Should().OnlyContain(e => e.Player == _alice);
        captured.Should().OnlyContain(e => !e.WasCost, "a cleanup trim is not a cost");
        _alice.Zones.Hand.GetCards().Should().HaveCount(7);
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void CleanupStep_DiscardToHandSize_UnderMax_PublishesNothing()
    {
        var bus = new EventBus();
        var captured = new List<DiscardedEvent>();
        bus.Subscribe<DiscardedEvent>(captured.Add);

        var zoneService = new Majik.Core.Services.ZoneService();
        for (var i = 0; i < 5; i++) NewCardInHand(_alice, $"H{i}");

        var cleanup = new CleanupStep(bus, zoneService);
        cleanup.DiscardToHandSize(_alice, maxHandSize: 7);

        captured.Should().BeEmpty("5 cards is under the 7-card max — no discard");
    }

    // -----------------------------------------------------------------------
    // Triggers.OnDiscard — "you discard" gate
    // -----------------------------------------------------------------------

    [Fact]
    public void TriggersOnDiscard_FiresForMatchingPlayer_NotForOpponent()
    {
        var cond = Triggers.OnDiscard(_alice);
        var card = new Card("C", "");

        cond.Matches(new DiscardedEvent(_alice, card, wasCost: false), null!).Should().BeTrue();
        cond.Matches(new DiscardedEvent(_alice, card, wasCost: true), null!).Should().BeTrue();
        cond.Matches(new DiscardedEvent(_bob, card, wasCost: false), null!).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Flameblade Adept integration — pumps on YOUR discard, not opponent's
    // -----------------------------------------------------------------------

    [Fact]
    public void FlamebladeAdept_PumpsOnYourDiscard_ViaBus_NotOpponents()
    {
        var bus = new EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService();

        var adept = Majik.Core.CardData.Factories.FlamebladeAdeptFactory.Create(
            _alice, effects, triggers: null);
        _alice.Zones.Battlefield.AddCard(adept);
        adept.SetZone(ZoneType.Battlefield);

        var discardTrigger = adept.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<DiscardedEvent>);

        // Minimal trigger pump: on a matching DiscardedEvent, resolve the
        // trigger's effects (the engine's TriggerManager does this for real).
        bus.Subscribe<DiscardedEvent>(e =>
        {
            if (discardTrigger.Condition.Matches(e, discardTrigger))
                foreach (var eff in discardTrigger.Effects) eff.Execute();
        });

        adept.Power.Should().Be(1, "base power before any discard");

        // Opponent discards — no pump.
        bus.Publish(new DiscardedEvent(_bob, new Card("Opp", ""), wasCost: false));
        adept.Power.Should().Be(1, "opponent's discard does not pump Flameblade Adept");

        // Controller discards — +1/+0.
        bus.Publish(new DiscardedEvent(_alice, new Card("Mine", ""), wasCost: false));
        adept.Power.Should().Be(2, "controller's discard pumps Flameblade Adept +1/+0");
    }
}
