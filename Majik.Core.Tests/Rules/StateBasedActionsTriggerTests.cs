using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// State-change triggered abilities (Rule 603.2c) are evaluated alongside SBAs.
/// </summary>
public class StateBasedActionsTriggerTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly StateBasedActions _sba;

    public StateBasedActionsTriggerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _sba = new StateBasedActions(_bus, triggerManager: _triggers);
    }

    [Fact]
    public void CheckStateBasedActions_FiresStateChangeTrigger_WhenConditionRises()
    {
        var threshold = false;
        var source = new Creature("Watcher", "1W", 1, 1) { Owner = _alice, Zone = ZoneType.Battlefield };
        var stateChange = new StateChangeTriggerCondition(() => threshold);
        var ability = new TriggeredAbility(source, _alice, stateChange);
        _triggers.RegisterTriggeredAbility(ability);

        // Threshold not met yet — no trigger.
        _sba.CheckStateBasedActions(new[] { _alice }, Array.Empty<ICard>());
        _triggers.PendingCount.Should().Be(0);

        // Threshold flips true — trigger fires once.
        threshold = true;
        _sba.CheckStateBasedActions(new[] { _alice }, Array.Empty<ICard>());
        _triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public void CheckStateBasedActions_DoesNotRefire_WhileConditionStaysTrue()
    {
        var source = new Creature("Watcher", "1W", 1, 1) { Owner = _alice, Zone = ZoneType.Battlefield };
        var ability = new TriggeredAbility(source, _alice,
            new StateChangeTriggerCondition(() => true));
        _triggers.RegisterTriggeredAbility(ability);

        _sba.CheckStateBasedActions(new[] { _alice }, Array.Empty<ICard>());
        _sba.CheckStateBasedActions(new[] { _alice }, Array.Empty<ICard>());

        _triggers.PendingCount.Should().Be(1);
    }
}
