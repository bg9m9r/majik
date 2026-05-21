using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

/// <summary>
/// Tests for CR 702.93 — Undying keyword, implemented via
/// <see cref="UndyingFactory"/>.
/// </summary>
public class UndyingTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    private Creature MakeWolf()
    {
        var wolf = new Creature("Young Wolf", "G", 1, 1)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(wolf);
        return wolf;
    }

    /// <summary>
    /// CR 702.93b — dies with no +1/+1 counters → returns to battlefield
    /// under owner's control with one +1/+1 counter.
    /// </summary>
    [Fact]
    public void Undying_CreatureDiesWithNoCounters_ReturnsToBattlefieldWithCounter()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wolf = MakeWolf();
        wolf.AddAbility(UndyingFactory.Build(wolf));
        triggers.BindCard(wolf);

        // Simulate death via ZoneService (moves wolf to graveyard, fires CardMovedEvent).
        zones.MoveCardTo(wolf, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "Undying trigger must queue on death");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Wolf should be back on the battlefield.
        wolf.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(wolf);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(wolf);

        // Wolf should have exactly one +1/+1 counter (becoming a 2/2).
        wolf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    /// <summary>
    /// CR 702.93 — "if it had no +1/+1 counters on it": a creature that already
    /// carried a +1/+1 counter when it died does NOT return. The intervening-if
    /// condition (CR 603.4) gates the trigger going on the stack.
    /// </summary>
    [Fact]
    public void Undying_CreatureDiesWithPlusOneCounter_StaysInGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wolf = MakeWolf();
        wolf.AddAbility(UndyingFactory.Build(wolf));
        triggers.BindCard(wolf);

        // Give wolf a +1/+1 counter before it dies.
        wolf.Counters.Add(CounterType.PlusOnePlusOne, 1);
        wolf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // Die.
        zones.MoveCardTo(wolf, ZoneType.Graveyard);

        // InterveningIf fails — trigger must NOT go on the stack.
        triggers.PendingCount.Should().Be(0, "Undying must not trigger when +1/+1 counter was present");

        wolf.Zone.Should().Be(ZoneType.Graveyard);
    }

    /// <summary>
    /// After an Undying return (creature now has the +1/+1 counter), a
    /// second death must NOT trigger Undying again (CR 702.93 — the
    /// intervening-if condition fails because the counter is present).
    /// </summary>
    [Fact]
    public void Undying_AfterReturn_SecondDeathDoesNotTrigger()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wolf = MakeWolf();
        wolf.AddAbility(UndyingFactory.Build(wolf));
        triggers.BindCard(wolf);

        // First death — no counter.
        zones.MoveCardTo(wolf, ZoneType.Graveyard);
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Wolf is back on battlefield with +1/+1 counter.
        wolf.Zone.Should().Be(ZoneType.Battlefield);
        wolf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // Register it again with the trigger manager (TriggerManager re-syncs
        // registrations as the card re-enters the battlefield zone).
        // The BindCard call is idempotent — it re-syncs active zone membership.
        triggers.BindCard(wolf);

        // Second death — now has the +1/+1 counter from the Undying return.
        zones.MoveCardTo(wolf, ZoneType.Graveyard);

        // Trigger is queued (event fired) but InterveningIf fails when going on stack.
        // PutPendingTriggersOnStack calls CanBePutOnStack() which checks InterveningIf.
        var pendingBefore = triggers.PendingCount;
        triggers.PutPendingTriggersOnStack(_alice);

        // Nothing should resolve — wolf stays dead.
        stack.IsEmpty.Should().BeTrue(
            "Undying must not return the creature a second time after it already returned with a counter");
        wolf.Zone.Should().Be(ZoneType.Graveyard);
    }
}
