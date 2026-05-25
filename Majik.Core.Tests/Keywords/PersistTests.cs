using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Tests for CR 702.79 — Persist keyword, implemented via
/// <see cref="PersistFactory"/>. Mirror of the Undying / Modular shape tests.
///
/// Covers:
///   - Keyword marker attached ("Persist").
///   - Death trigger attached to the card shape with the correct active zones
///     (Battlefield + Graveyard).
///   - Dies with no -1/-1 counter → returns with one.
///   - Dies WITH a -1/-1 counter → interveningIf fails, stays dead.
///   - Multiple Persist creatures each return independently.
///   - Replacement-bus path stamps the -1/-1 counter through CountersService
///     (placeholder for negative-counter replacements — symmetric with Undying
///     / Modular).
/// </summary>
public class PersistTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakePersistBeast(string name, Player owner,
        ReplacementBus? replacements = null)
    {
        var c = new Creature(name, "{2}", 2, 2, subtypes: new[] { CardSubtype.Beast });
        c.SetOwner(owner);
        c.SetController(owner);
        PersistFactory.Build(c, replacements);
        return c;
    }

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    // -------------------------------------------------------------------------
    // 1. Keyword marker + trigger shape
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_AttachesKeywordMarker()
    {
        var beast = MakePersistBeast("Persist Test", _alice);

        var marker = beast.Abilities.OfType<KeywordAbility>().SingleOrDefault();
        marker.Should().NotBeNull("Persist ships a KeywordAbility marker so inspectors can see it");
        marker!.Keyword.Should().Be("Persist");
    }

    [Fact]
    public void Build_AttachesDeathTrigger_WithBothActiveZones()
    {
        var beast = MakePersistBeast("Persist Test", _alice);

        var trig = beast.Abilities.OfType<TriggeredAbility>().SingleOrDefault();
        trig.Should().NotBeNull("Persist primitive attaches one death trigger at construction");
        trig!.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trig.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "Graveyard must be in ActiveZones — the trigger evaluates after the death zone-move");
    }

    [Fact]
    public void Build_NullSource_Throws()
    {
        var act = () => PersistFactory.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------
    // 2. Dies with no -1/-1 counter → returns with one
    // -------------------------------------------------------------------------

    [Fact]
    public void DiesWithNoCounter_ReturnsToBattlefieldWithMinusCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var beast = MakePersistBeast("Persist Test", _alice);
        PutOnBattlefield(_alice, beast);
        triggers.BindCard(beast);

        // Simulate death via ZoneService.
        zones.MoveCardTo(beast, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1,
            "Persist trigger must queue on death without -1/-1 counter");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        beast.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(beast);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(beast);
        beast.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "Persist places exactly one -1/-1 counter on the returning creature");
    }

    // -------------------------------------------------------------------------
    // 3. Dies WITH a -1/-1 counter → interveningIf fails
    // -------------------------------------------------------------------------

    [Fact]
    public void DiesWithMinusCounter_InterveningIfFails_StaysInGraveyard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var beast = MakePersistBeast("Persist Test", _alice);
        PutOnBattlefield(_alice, beast);
        triggers.BindCard(beast);

        beast.Counters.Add(CounterType.MinusOneMinusOne, 1);

        zones.MoveCardTo(beast, ZoneType.Graveyard);

        // Trigger may queue, but interveningIf gates it from going on the stack.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.IsEmpty.Should().BeTrue(
            "Persist must not put a trigger on the stack when a -1/-1 counter is present at death");
        beast.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void SecondDeathAfterPersistReturn_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var beast = MakePersistBeast("Persist Test", _alice);
        PutOnBattlefield(_alice, beast);
        triggers.BindCard(beast);

        // First death — no counter; Persist returns.
        zones.MoveCardTo(beast, ZoneType.Graveyard);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        beast.Zone.Should().Be(ZoneType.Battlefield);
        beast.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);

        // Re-bind after raw zone-move.
        triggers.BindCard(beast);

        // Second death — has the -1/-1 counter.
        zones.MoveCardTo(beast, ZoneType.Graveyard);
        triggers.PutPendingTriggersOnStack(_alice);

        stack.IsEmpty.Should().BeTrue(
            "Persist must not re-trigger when a -1/-1 counter was present at the second death");
        beast.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -------------------------------------------------------------------------
    // 4. Multiple Persist creatures return independently
    // -------------------------------------------------------------------------

    [Fact]
    public void MultiplePersistDeaths_EachReturnsIndependently()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var first = MakePersistBeast("Persist A", _alice);
        var second = MakePersistBeast("Persist B", _alice);
        PutOnBattlefield(_alice, first);
        PutOnBattlefield(_alice, second);
        triggers.BindCard(first);
        triggers.BindCard(second);

        zones.MoveCardTo(first, ZoneType.Graveyard);
        zones.MoveCardTo(second, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(2,
            "each Persist death queues its own trigger");
        triggers.PutPendingTriggersOnStack(_alice);

        // Resolve all stacked triggers.
        while (!stack.IsEmpty)
        {
            stack.Pop()!.Resolve();
        }

        first.Zone.Should().Be(ZoneType.Battlefield);
        second.Zone.Should().Be(ZoneType.Battlefield);
        first.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        second.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // 5. ReplacementBus path
    // -------------------------------------------------------------------------

    [Fact]
    public void DeathTrigger_WithBus_AddsCounterViaCountersService()
    {
        // The Persist return-side counter add routes through
        // CountersService.Add with the supplied bus. No -1/-1 counter
        // replacement exists today; this test pins the placeholder route so
        // future Hardened-Scales-equivalent bumps will be picked up.
        var bus = new ReplacementBus();
        var beast = MakePersistBeast("Persist Test", _alice, replacements: bus);

        PutOnBattlefield(_alice, beast);
        // Simulate death (raw zone-move, no need to spin the full pipeline).
        _alice.Zones.Battlefield.RemoveCard(beast);
        _alice.Zones.Graveyard.AddCard(beast);
        beast.SetZone(ZoneType.Graveyard);

        var persist = beast.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in persist.Effects) e.Execute();

        beast.Zone.Should().Be(ZoneType.Battlefield);
        beast.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "with no replacement registered, the counter lands at the printed value (1)");
    }
}
