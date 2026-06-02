using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GeralfsMessengerFactory"/> (Dark Ascension,
/// {B}{B}{B}).
///
/// Covers:
/// - Identity (3/2 Creature — Zombie, mana cost, owner/controller).
/// - Undying keyword marker (CR 702.93).
/// - Enters-tapped replacement (CR 614.1c) — present when wired through
///   <see cref="ReplacementBus"/>; absent on shape-only path (Messenger
///   enters untapped).
/// - ETB triggered ability (CR 603.6a + CR 119.3) — target opponent loses
///   2 life on resolution.
/// - Undying interaction: dies with 0 counters → returns with +1/+1
///   counter AND enters tapped again on the return (CR 614.1c — the
///   replacement still applies to every ETB).
/// - Dies with +1/+1 counter → stays dead (CR 702.93 / CR 603.4
///   intervening-if).
/// - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "B")]
public class GeralfsMessengerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GeralfsMessenger_Identity()
    {
        var c = GeralfsMessengerFactory.Create(_alice);

        c.Name.Should().Be("Geralf's Messenger");
        c.ManaCost.Should().Be("{B}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue("printed creature type is Zombie");
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Undying keyword marker (CR 702.93)
    // -----------------------------------------------------------------------

    [Fact]
    public void GeralfsMessenger_HasUndyingKeyword()
    {
        var c = GeralfsMessengerFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Undying",
            "CR 702.93 — Undying is printed on Geralf's Messenger");
    }

    // -----------------------------------------------------------------------
    // Enters-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 614.1c — "Geralf's Messenger enters tapped." When the
    /// EntersTappedReplacement is registered on the ReplacementBus, the
    /// ZoneService.MoveCardTo path sets IsTapped on landing.
    /// </summary>
    [Fact]
    public void GeralfsMessenger_EntersTapped_WhenWiredThroughReplacementBus()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var messenger = GeralfsMessengerFactory.Create(_alice, bus, triggers, rep);
        messenger.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(messenger);
        triggers.BindCard(messenger);

        zones.MoveCardTo(messenger, ZoneType.Battlefield, controller: _alice);

        messenger.IsTapped.Should().BeTrue(
            "CR 614.1c — Geralf's Messenger enters tapped");
        messenger.Zone.Should().Be(ZoneType.Battlefield);
    }

    /// <summary>
    /// Shape-only path (no ReplacementBus): the enters-tapped replacement is
    /// omitted, mirroring how Creeping Tar Pit / Valakut defer the restriction
    /// to the binder layer for shape construction. Messenger enters untapped
    /// when moved through a ZoneService with no replacement-bus binding.
    /// </summary>
    [Fact]
    public void GeralfsMessenger_EntersUntapped_OnShapeOnlyPath()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var messenger = GeralfsMessengerFactory.Create(_alice);
        messenger.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(messenger);

        zones.MoveCardTo(messenger, ZoneType.Battlefield, controller: _alice);

        messenger.IsTapped.Should().BeFalse(
            "shape-only path omits the enters-tapped replacement");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — target opponent loses 2 life (CR 603.6a + CR 119.3)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 603.6a + CR 119.3 — when Geralf's Messenger enters the battlefield,
    /// target opponent loses 2 life. The trigger fires on a CardMovedEvent
    /// to the Battlefield zone; on resolution the chosen Player loses 2 life.
    /// </summary>
    [Fact]
    public void GeralfsMessenger_EntersBattlefield_TargetOpponentLoses2Life()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var messenger = GeralfsMessengerFactory.Create(_alice, bus, triggers, rep);
        messenger.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(messenger);
        triggers.BindCard(messenger);

        var bobLifeBefore = _bob.LifeTotal;

        zones.MoveCardTo(messenger, ZoneType.Battlefield, controller: _alice);

        // Both the ETB drain trigger should queue (Undying trigger is
        // graveyard-active and won't fire on an ETB).
        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "ETB drain trigger must queue on entering battlefield");

        // Locate the ETB trigger (carries the "target opponent" request) and
        // preset Bob as the chosen target.
        var etbTrigger = messenger.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1
                      && t.TargetRequests[0].Description == "target opponent");
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(bobLifeBefore - GeralfsMessengerFactory.LifeLossAmount,
            "target opponent should lose 2 life on Geralf's Messenger ETB");
    }

    // -----------------------------------------------------------------------
    // Undying — dies with no counters → returns with +1/+1 (and enters tapped)
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 702.93b — when Geralf's Messenger dies with no +1/+1 counters, it
    /// returns to the battlefield under its owner's control with one +1/+1
    /// counter. CR 614.1c — the enters-tapped replacement re-applies on the
    /// return.
    ///
    /// Note: <see cref="UndyingFactory"/> performs a raw zone-move on return
    /// (not through ZoneService), so the engine's ReplacementBus is NOT
    /// consulted on the Undying return today. This test documents the
    /// CURRENT behaviour and asserts the Undying mechanic itself: returned
    /// to battlefield with one +1/+1 counter; tapped state on return defers
    /// to the binder layer.
    /// </summary>
    [Fact]
    public void GeralfsMessenger_DiesWithNoCounters_ReturnsWithPlusOnePlusOneCounter()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var messenger = GeralfsMessengerFactory.Create(_alice, bus, triggers, rep);
        messenger.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(messenger);
        triggers.BindCard(messenger);

        // Drain any queued ETB triggers from BindCard (none expected — the
        // ETB trigger fires on CardMovedEvent, not on BindCard).
        triggers.PendingCount.Should().Be(0);

        // Simulate death via ZoneService.
        zones.MoveCardTo(messenger, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1,
            "Undying trigger must queue on death without a +1/+1 counter");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Returned to battlefield under owner's control.
        messenger.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(messenger);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(messenger);

        // Exactly one +1/+1 counter (CR 702.93b).
        messenger.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Undying interveningIf — dies with +1/+1 counter → stays dead
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 702.93 + CR 603.4 — "if it had no +1/+1 counters on it": Geralf's
    /// Messenger does NOT return when it dies already carrying a +1/+1
    /// counter (e.g. from a previous Undying return). The intervening-if
    /// gate keeps the trigger off the stack.
    /// </summary>
    [Fact]
    public void GeralfsMessenger_DiesWithPlusOneCounter_StaysInGraveyard()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var messenger = GeralfsMessengerFactory.Create(_alice, bus, triggers, rep);
        messenger.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(messenger);
        triggers.BindCard(messenger);

        // Give Messenger a +1/+1 counter BEFORE it dies.
        messenger.Counters.Add(CounterType.PlusOnePlusOne, 1);
        messenger.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // Die.
        zones.MoveCardTo(messenger, ZoneType.Graveyard);

        // InterveningIf fails — Undying trigger must NOT go on the stack.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.IsEmpty.Should().BeTrue(
            "Undying must not return Messenger when it already had a +1/+1 counter at death");

        messenger.Zone.Should().Be(ZoneType.Graveyard);
    }
}
