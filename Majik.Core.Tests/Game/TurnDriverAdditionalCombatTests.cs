using FluentAssertions;
using Majik.Core.Game;
using Xunit;

public class TurnDriverAdditionalCombatTests
{
    // Focused unit test on the new exposed queue; full integration with
    // RunCombat is covered by the existing CombatFlow tests, which
    // remain green.

    [Fact]
    public void AdditionalCombats_StartsEmpty_AccumulatesEnqueues()
    {
        var q = new AdditionalCombatQueue();
        q.Pending.Should().Be(0);
        q.HasAdditional.Should().BeFalse();
        q.EnqueueAdditional();
        q.EnqueueAdditional();
        q.Pending.Should().Be(2);
        q.HasAdditional.Should().BeTrue();
    }

    [Fact]
    public void AdditionalCombats_ConsumesUntilEmpty()
    {
        var q = new AdditionalCombatQueue();
        q.EnqueueAdditional();
        q.TryConsume().Should().BeTrue();
        q.TryConsume().Should().BeFalse();
    }
}
