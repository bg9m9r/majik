using FluentAssertions;
using Majik.Core.Abilities;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Abilities;

public class StateChangeTriggerConditionTests
{
    private readonly Mock<ITriggeredAbility> _ability = new();

    [Fact]
    public void IsSatisfied_ReturnsTrue_OnRisingEdge()
    {
        var live = false;
        var cond = new StateChangeTriggerCondition(() => live);

        cond.IsSatisfied().Should().BeFalse();
        live = true;
        cond.IsSatisfied().Should().BeTrue();
    }

    [Fact]
    public void IsSatisfied_DoesNotFireAgain_WhileConditionStaysTrue()
    {
        var live = true;
        var cond = new StateChangeTriggerCondition(() => live);

        cond.IsSatisfied().Should().BeTrue();
        cond.IsSatisfied().Should().BeFalse();
        cond.IsSatisfied().Should().BeFalse();
    }

    [Fact]
    public void IsSatisfied_RefiresAfterFallingThenRisingEdge()
    {
        var live = true;
        var cond = new StateChangeTriggerCondition(() => live);

        cond.IsSatisfied().Should().BeTrue();   // rising
        cond.IsSatisfied().Should().BeFalse();  // still true
        live = false;
        cond.IsSatisfied().Should().BeFalse();  // false now
        live = true;
        cond.IsSatisfied().Should().BeTrue();   // rising again
    }

    [Fact]
    public void EventType_IsStateChangeSentinel()
    {
        var cond = new StateChangeTriggerCondition(() => true);

        cond.EventType.Should().Be(typeof(StateChangeTriggerCondition));
    }

    [Fact]
    public void Matches_AlwaysFalse_ForGameEvents()
    {
        // State-change triggers are never event-driven; TriggerManager
        // evaluates them via a separate path during SBA passes.
        var cond = new StateChangeTriggerCondition(() => true);
        var fakeEvent = new Mock<Majik.Core.Events.GameEvent>(Majik.Core.Events.EventType.Triggered).Object;

        cond.Matches(fakeEvent, _ability.Object).Should().BeFalse();
    }
}
