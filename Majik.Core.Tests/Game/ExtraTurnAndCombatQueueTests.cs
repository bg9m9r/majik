using FluentAssertions;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

public class ExtraTurnAndCombatQueueTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ExtraTurnQueue_DequeuesInLifoOrder()
    {
        var q = new ExtraTurnQueue();
        q.EnqueueExtraTurn(_alice);
        q.EnqueueExtraTurn(_bob);

        q.TryDequeueNext(out var first).Should().BeTrue();
        first.Should().BeSameAs(_bob); // last-added taken first
        q.TryDequeueNext(out var second).Should().BeTrue();
        second.Should().BeSameAs(_alice);
        q.TryDequeueNext(out _).Should().BeFalse();
    }

    [Fact]
    public void ExtraTurnQueue_EmptyDequeue_ReturnsFalse()
    {
        var q = new ExtraTurnQueue();
        q.TryDequeueNext(out var p).Should().BeFalse();
        p.Should().BeNull();
    }

    [Fact]
    public void AdditionalCombatQueue_ConsumesUntilEmpty()
    {
        var q = new AdditionalCombatQueue();
        q.EnqueueAdditional();
        q.EnqueueAdditional();

        q.HasAdditional.Should().BeTrue();
        q.TryConsume().Should().BeTrue();
        q.TryConsume().Should().BeTrue();
        q.TryConsume().Should().BeFalse();
        q.HasAdditional.Should().BeFalse();
    }

    [Fact]
    public void AdditionalCombatQueue_Reset_ClearsPending()
    {
        var q = new AdditionalCombatQueue();
        q.EnqueueAdditional();
        q.EnqueueAdditional();
        q.Reset();
        q.Pending.Should().Be(0);
    }
}
