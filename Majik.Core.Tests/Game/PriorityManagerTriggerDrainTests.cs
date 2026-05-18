using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// PriorityManager must drain pending triggers onto the stack BEFORE granting
/// priority (Rule 603.3 — triggered abilities go on the stack the next time a
/// player would receive priority).
/// </summary>
public class PriorityManagerTriggerDrainTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly PriorityManager _priority;

    public PriorityManagerTriggerDrainTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public void InitializeForPhase_DrainsPendingTriggers_BeforeGivingPriority()
    {
        EnqueueEtbTrigger(_alice);
        _triggers.PendingCount.Should().Be(1);

        _priority.InitializeForPhase(_alice);

        _triggers.PendingCount.Should().Be(0);
        _stack.Count.Should().Be(1);
    }

    [Fact]
    public void PassPriority_DrainsNewlyPendingTriggers_BeforeNextPlayerGetsPriority()
    {
        _priority.InitializeForPhase(_alice);
        EnqueueEtbTrigger(_alice);

        _priority.PassPriority();

        _triggers.PendingCount.Should().Be(0);
        _stack.Count.Should().Be(1);
    }

    [Fact]
    public void InitializeForPhase_NoPendingTriggers_PushesNothing()
    {
        _priority.InitializeForPhase(_alice);

        _stack.IsEmpty.Should().BeTrue();
    }

    private void EnqueueEtbTrigger(Player controller)
    {
        var source = new Creature($"S-{Guid.NewGuid()}", "1G", 2, 2)
        {
            Owner = controller, Zone = ZoneType.Battlefield,
        };
        var ability = new TriggeredAbility(source, controller,
            Triggers.OnEnterBattlefieldSelf(source));
        _triggers.RegisterTriggeredAbility(ability);
        _bus.Publish(new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield));
    }
}
