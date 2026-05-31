using FluentAssertions;
using Majik.Core.Abilities;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// PLAN 01 (Slice A) — the back-compat adapter on <see cref="Effect"/>.
/// Verifies the legacy <c>Effect(string, Action)</c> ctor runs through the
/// async <see cref="IEffect.ExecuteAsync"/> path, the new async ctor is
/// awaited, and the synchronous <see cref="IEffect.Execute"/> shim still
/// works on both ctors.
/// </summary>
public class EffectAdapterTests
{
    [Fact]
    public async Task LegacyActionCtor_RunsThroughExecuteAsync()
    {
        var ran = false;
        var effect = new Effect("legacy", () => ran = true);

        await effect.ExecuteAsync(ResolutionContext.Legacy);

        ran.Should().BeTrue();
    }

    [Fact]
    public void LegacyActionCtor_StillRunsThroughSyncExecuteShim()
    {
        var ran = false;
        var effect = new Effect("legacy", () => ran = true);

        effect.Execute();

        ran.Should().BeTrue();
    }

    [Fact]
    public async Task AsyncCtor_IsAwaited_AndReceivesContext()
    {
        ResolutionContext? seen = null;
        var effect = new Effect("async", async ctx =>
        {
            await Task.Yield();
            seen = ctx;
        });

        await effect.ExecuteAsync(ResolutionContext.Legacy);

        seen.Should().BeSameAs(ResolutionContext.Legacy);
    }

    [Fact]
    public void AsyncCtor_RunsThroughSyncExecuteShim()
    {
        var ran = false;
        var effect = new Effect("async", ctx =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });

        effect.Execute();

        ran.Should().BeTrue();
    }

    [Fact]
    public void NullDescription_Throws_OnBothCtors()
    {
        var sync = () => new Effect(null!, () => { });
        var async = () => new Effect(null!, _ => ValueTask.CompletedTask);

        sync.Should().Throw<ArgumentNullException>();
        async.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NullBody_Throws_OnBothCtors()
    {
        var sync = () => new Effect("x", (Action)null!);
        var async = () => new Effect("x", (Func<ResolutionContext, ValueTask>)null!);

        sync.Should().Throw<ArgumentNullException>();
        async.Should().Throw<ArgumentNullException>();
    }
}
