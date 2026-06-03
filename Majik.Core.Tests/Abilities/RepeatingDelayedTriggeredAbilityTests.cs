using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// CR 603.7e — turn-scoped REPEATING delayed triggered abilities ("until end
/// of turn, whenever X happens, do Y"). Unlike a one-shot
/// <see cref="DelayedTriggeredAbility"/>, the repeating variant stays
/// registered and fires every time its event recurs, until torn down at
/// end-of-turn cleanup.
/// </summary>
public class RepeatingDelayedTriggeredAbilityTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _manager;
    private readonly Player _alice = new("Alice", 20);

    public RepeatingDelayedTriggeredAbilityTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _manager = new TriggerManager(_stack, _bus);
    }

    private RepeatingDelayedTriggeredAbility BuildCountingTrigger(out Func<int> reader)
    {
        var card = new Instant("Source", "1") { Owner = _alice };
        var fires = 0;
        reader = () => fires;
        return new RepeatingDelayedTriggeredAbility(
            card, _alice,
            Triggers.OnCardDrawnByPlayer(_alice),
            effects: new IEffect[] { new Effect("count", () => fires++) });
    }

    [Fact]
    public void RepeatingDelayedTrigger_FiresEveryTime_AndStaysRegistered()
    {
        var ability = BuildCountingTrigger(out var fires);
        _manager.RegisterDelayed(ability);

        // First draw — fires AND stays registered.
        _bus.Publish(new CardDrawnEvent(new Instant("X", "1"), _alice));
        _manager.PendingCount.Should().Be(1);
        _manager.PutPendingTriggersOnStack(_alice);
        _stack.Pop()!.Resolve();
        fires().Should().Be(1);
        _manager.IsRegistered(ability).Should().BeTrue(
            because: "a repeating delayed trigger is NOT unregistered after firing (CR 603.7e)");

        // Second draw — fires AGAIN (the differentiator from the one-shot path).
        _bus.Publish(new CardDrawnEvent(new Instant("Y", "1"), _alice));
        _manager.PendingCount.Should().Be(1);
        _manager.PutPendingTriggersOnStack(_alice);
        _stack.Pop()!.Resolve();
        fires().Should().Be(2);
        _manager.IsRegistered(ability).Should().BeTrue();
    }

    [Fact]
    public void RepeatingDelayedTrigger_ExpiresAtEndOfTurnCleanup()
    {
        var ability = BuildCountingTrigger(out var fires);
        _manager.RegisterDelayed(ability);

        _bus.Publish(new CardDrawnEvent(new Instant("X", "1"), _alice));
        _manager.PutPendingTriggersOnStack(_alice);
        _stack.Pop()!.Resolve();
        fires().Should().Be(1);

        // CR 603.7e / CR 514.2 — end-of-turn cleanup tears it down.
        _manager.ExpireTurnScopedDelayedTriggers();
        _manager.IsRegistered(ability).Should().BeFalse();

        // After cleanup it no longer fires.
        _bus.Publish(new CardDrawnEvent(new Instant("Y", "1"), _alice));
        _manager.PendingCount.Should().Be(0);
        fires().Should().Be(1);
    }

    [Fact]
    public void ExpireTurnScoped_AlsoDropsAlreadyQueuedPendingInstances()
    {
        var ability = BuildCountingTrigger(out var fires);
        _manager.RegisterDelayed(ability);

        // Queue an instance but do NOT drain it onto the stack.
        _bus.Publish(new CardDrawnEvent(new Instant("X", "1"), _alice));
        _manager.PendingCount.Should().Be(1);

        // Cleanup drops the pending, un-resolved instance too.
        _manager.ExpireTurnScopedDelayedTriggers();
        _manager.PendingCount.Should().Be(0);
        fires().Should().Be(0);
    }

    [Fact]
    public void ExpireTurnScoped_LeavesOneShotDelayedTriggersUntouched()
    {
        var card = new Instant("Source", "1") { Owner = _alice };
        var oneShot = new DelayedTriggeredAbility(
            card, _alice, Triggers.OnCardDrawnByPlayer(_alice));
        _manager.RegisterDelayed(oneShot);

        _manager.ExpireTurnScopedDelayedTriggers();

        _manager.IsRegistered(oneShot).Should().BeTrue(
            because: "ExpireTurnScopedDelayedTriggers only removes the REPEATING variant");
    }

    [Fact]
    public void RepeatingDelayedTrigger_HasNoZoneRestriction_ByDefault()
    {
        var card = new Instant("Source", "1") { Owner = _alice, Zone = ZoneType.Graveyard };
        var ability = new RepeatingDelayedTriggeredAbility(
            card, _alice, Triggers.OnCardDrawnByPlayer(_alice));

        ability.ActiveZones.Should().Contain(ZoneType.Graveyard,
            because: "delayed triggers fire regardless of source zone (CR 603.7d)");
    }
}
