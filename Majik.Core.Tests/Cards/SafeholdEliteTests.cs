using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Tests for Safehold Elite (Shadowmoor, {1}{G/W}):
///   - Shape: 2/2 Creature — Elf Scout, mana cost {1}{G/W}.
///   - Persist (CR 702.79): dies without -1/-1 counter → returns with one.
///   - Persist interveningIf (CR 603.4): dies WITH a -1/-1 counter → stays dead.
///   - Second death after a Persist return: stays dead.
///
/// Safehold Elite is a vanilla Persist body (no ETB rider), so it mirrors
/// Kitchen Finks' Persist behaviour without the lifegain trigger. The base
/// shape is materialised from the embedded JSON definition; the Persist
/// mechanic is layered on via <see cref="Majik.Core.Keywords.PersistFactory"/>.
/// </summary>
public class SafeholdEliteTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    private Creature MakeElite()
    {
        var elite = SafeholdEliteFactory.Create(_alice);
        elite.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(elite);
        return elite;
    }

    // ------------------------------------------------------------------
    // Shape
    // ------------------------------------------------------------------

    /// <summary>
    /// Base shape from the JSON definition — 2/2 Creature — Elf Scout,
    /// mana cost {1}{G/W}, with the Persist keyword marker (CR 702.79).
    /// </summary>
    [Fact]
    public void SafeholdElite_HasExpectedShape()
    {
        var elite = SafeholdEliteFactory.Create(_alice);

        elite.Name.Should().Be("Safehold Elite");
        elite.Power.Should().Be(2);
        elite.Toughness.Should().Be(2);
        elite.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        elite.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        elite.ManaCost.ToString().Should().Be("{1}{G/W}");
    }

    // ------------------------------------------------------------------
    // Persist — dies without -1/-1 counter → returns with one
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 702.79 — when Safehold Elite dies with no -1/-1 counters, it returns
    /// to the battlefield under its owner's control with one -1/-1 counter.
    /// </summary>
    [Fact]
    public void SafeholdElite_DiesWithNoMinusCounters_ReturnsToBattlefieldWithCounter()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var elite = MakeElite();
        triggers.BindCard(elite);

        // Simulate death via ZoneService (moves to graveyard, fires CardMovedEvent).
        zones.MoveCardTo(elite, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "Persist trigger must queue on death without -1/-1 counter");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Elite should be back on the battlefield.
        elite.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(elite);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(elite);

        // Exactly one -1/-1 counter (becoming a 1/1).
        elite.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        elite.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "counter bag should be clean — only the one -1/-1 counter added by Persist");
    }

    // ------------------------------------------------------------------
    // Persist interveningIf — dies WITH -1/-1 counter → stays dead
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 702.79 + CR 603.4 — "if it had no -1/-1 counters on it": a creature
    /// that already had a -1/-1 counter when it died does NOT return.
    /// </summary>
    [Fact]
    public void SafeholdElite_DiesWithMinusCounter_StaysInGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var elite = MakeElite();
        triggers.BindCard(elite);

        // Give Elite a -1/-1 counter before it dies.
        elite.Counters.Add(CounterType.MinusOneMinusOne, 1);
        elite.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);

        zones.MoveCardTo(elite, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(0, "Persist must not trigger when -1/-1 counter was present at death");
        elite.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ------------------------------------------------------------------
    // Second death after Persist return: stays dead
    // ------------------------------------------------------------------

    /// <summary>
    /// After a Persist return (Elite now carries a -1/-1 counter), a second
    /// death must NOT trigger Persist again — the interveningIf fails.
    /// </summary>
    [Fact]
    public void SafeholdElite_AfterPersistReturn_SecondDeathDoesNotTrigger()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var elite = MakeElite();
        triggers.BindCard(elite);

        // First death — no counter.
        zones.MoveCardTo(elite, ZoneType.Graveyard);
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        elite.Zone.Should().Be(ZoneType.Battlefield);
        elite.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);

        // Re-sync trigger manager after the raw Persist zone-move.
        triggers.BindCard(elite);

        // Second death — now has the -1/-1 counter from the Persist return.
        zones.MoveCardTo(elite, ZoneType.Graveyard);

        triggers.PutPendingTriggersOnStack(_alice);

        stack.IsEmpty.Should().BeTrue(
            "Persist must not return the creature a second time after it already returned with a -1/-1 counter");
        elite.Zone.Should().Be(ZoneType.Graveyard);
    }
}
