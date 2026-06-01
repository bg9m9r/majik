using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Effects;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// PLAN 08 — coverage for the async <see cref="ReplacementBus.ApplyAsync{TIntent}"/>
/// path and the async <see cref="LambdaReplacement{TIntent}"/> ctor. The key
/// correctness lever is the "deferred task" test: a replacement whose
/// <c>ReplaceAsync</c> returns a task that does NOT complete synchronously is
/// genuinely awaited by the bus (a sync-over-async bridge would block forever
/// on the un-signalled task; the async path completes once the task is
/// signalled).
/// </summary>
public class ReplacementBusAsyncTests
{
    public sealed record DamageIntent(int Amount, string Target);

    [Fact]
    public async Task ApplyAsync_NoEffects_ReturnsInputUnchanged()
    {
        var bus = new ReplacementBus();
        var intent = new DamageIntent(3, "Bob");

        var result = await bus.ApplyAsync(intent, ResolutionContext.Legacy);

        result.Should().Be(intent);
    }

    [Fact]
    public async Task ApplyAsync_NonPromptingReplacement_MatchesSyncApply()
    {
        // A replacement built from the SYNC-only ctor inherits the default
        // ReplaceAsync shim over Replace — ApplyAsync must produce the exact
        // same result as Apply.
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<DamageIntent>(
            (i, _) => i.Target == "Bob",
            (i, _) => i with { Amount = i.Amount - 1 }));

        var sync = bus.Apply(new DamageIntent(3, "Bob"));
        var async = await bus.ApplyAsync(new DamageIntent(3, "Bob"), ResolutionContext.Legacy);

        async!.Amount.Should().Be(2);
        async.Amount.Should().Be(sync!.Amount);
    }

    [Fact]
    public async Task ApplyAsync_DeferredReplacement_IsGenuinelyAwaited()
    {
        // The async replace body parks on a TaskCompletionSource that is NOT
        // signalled synchronously. If the bus bridged sync-over-async it would
        // block; the genuinely-async path returns a pending task we can
        // complete out-of-band.
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<DamageIntent>(
            applies: (_, _) => true,
            replace: (i, _) => i, // sync fallback (never hit on the async path)
            replaceAsync: async (i, _, _) =>
            {
                var delta = await gate.Task; // suspends until signalled
                return i with { Amount = i.Amount - delta };
            }));

        var applyTask = bus.ApplyAsync(new DamageIntent(10, "X"), ResolutionContext.Legacy);

        applyTask.IsCompleted.Should().BeFalse(
            "the replacement is awaiting an un-signalled task — the bus must not have bridged sync-over-async");

        gate.SetResult(4);
        var result = await applyTask;

        result!.Amount.Should().Be(6, "the awaited replacement debited 4");
    }

    [Fact]
    public async Task ApplyAsync_AsyncCtorCancellation_ReturnsNull()
    {
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<DamageIntent>(
            applies: (_, _) => true,
            replace: (i, _) => i,
            replaceAsync: (_, _, _) => new ValueTask<DamageIntent?>((DamageIntent?)null)));

        var result = await bus.ApplyAsync(new DamageIntent(3, "Bob"), ResolutionContext.Legacy);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_OneShotAsync_UnregistersAfterFiring()
    {
        var bus = new ReplacementBus();
        bus.Register(new LambdaReplacement<DamageIntent>(
            applies: (_, _) => true,
            replace: (i, _) => i with { Amount = 0 },
            replaceAsync: (i, _, _) => new ValueTask<DamageIntent?>(i with { Amount = 0 }),
            oneShot: true));

        (await bus.ApplyAsync(new DamageIntent(5, "X"), ResolutionContext.Legacy))!.Amount.Should().Be(0);
        (await bus.ApplyAsync(new DamageIntent(5, "X"), ResolutionContext.Legacy))!.Amount.Should().Be(5);
    }

    [Fact]
    public async Task ApplyAsync_NullContext_Throws()
    {
        var bus = new ReplacementBus();
        var act = async () => await bus.ApplyAsync(new DamageIntent(1, "X"), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
