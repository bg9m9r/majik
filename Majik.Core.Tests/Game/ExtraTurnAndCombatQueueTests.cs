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

    [Fact]
    public void AdditionalCombatQueue_DefaultEnqueue_IsCombatOnly()
    {
        // CR 506.4 — Combat Celebrant / Fear of Missing Out: an additional
        // combat phase with NO following main phase.
        var q = new AdditionalCombatQueue();
        q.EnqueueAdditional();

        q.TryConsume(out var followedByMain).Should().BeTrue();
        followedByMain.Should().BeFalse("combat-only grant has no following main phase");
    }

    [Fact]
    public void AdditionalCombatQueue_FollowedByMainGrant_IsReportedOnConsume()
    {
        // CR 505.1b — Relentless Assault / World at War: an additional combat
        // phase FOLLOWED BY an additional main phase.
        var q = new AdditionalCombatQueue();
        q.EnqueueAdditional(followedByMainPhase: true);

        q.TryConsume(out var followedByMain).Should().BeTrue();
        followedByMain.Should().BeTrue("this grant inserts a postcombat main after the extra combat");
    }

    [Fact]
    public void AdditionalCombatQueue_PreservesPerGrantFlags_InFifoOrder()
    {
        // CR 500.7 — extra phases are processed in creation order; each grant
        // keeps its own "followed by main" flag.
        var q = new AdditionalCombatQueue();
        q.EnqueueAdditional(followedByMainPhase: true);   // Relentless-Assault-like
        q.EnqueueAdditional(followedByMainPhase: false);  // Combat-Celebrant-like

        q.TryConsume(out var first).Should().BeTrue();
        first.Should().BeTrue("first-enqueued grant is consumed first (FIFO)");
        q.TryConsume(out var second).Should().BeTrue();
        second.Should().BeFalse();
        q.TryConsume(out _).Should().BeFalse();
    }
}
