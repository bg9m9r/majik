using FluentAssertions;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchEngineWatchdogTests
{
    /// <summary>Captures logged entries so a test can assert a faulted
    /// onWedged callback was observed + structured-logged rather than
    /// escaping as an UnobservedTaskException.</summary>
    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Ex)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private static MatchEngineWatchdog NewWatchdog(
        TimeSpan timeout, ILogger<MatchEngineWatchdog>? logger = null) =>
        new(logger ?? new CaptureLogger<MatchEngineWatchdog>(), timeout);

    [Fact]
    public async Task Arm_NeverBumped_FiresOnce()
    {
        var fired = new TaskCompletionSource<bool>();
        var calls = 0;
        var watchdog = NewWatchdog(TimeSpan.FromMilliseconds(80));
        var matchId = Guid.NewGuid();

        watchdog.Arm(matchId, () =>
        {
            Interlocked.Increment(ref calls);
            fired.TrySetResult(true);
            return Task.CompletedTask;
        });

        (await fired.Task.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        // Give any (erroneous) second timer a chance to fire.
        await Task.Delay(150);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Arm_RepeatedBumpThenStop_FiresOnceAfterFinalBump()
    {
        var fired = new TaskCompletionSource<bool>();
        var calls = 0;
        var watchdog = NewWatchdog(TimeSpan.FromMilliseconds(120));
        var matchId = Guid.NewGuid();

        watchdog.Arm(matchId, () =>
        {
            Interlocked.Increment(ref calls);
            fired.TrySetResult(true);
            return Task.CompletedTask;
        });

        // Bump faster than the timeout several times → must not fire yet.
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(40);
            watchdog.Bump(matchId);
        }
        calls.Should().Be(0, "repeated bumps faster than the timeout keep resetting the timer");

        // Stop bumping → fires once after the final bump + timeout.
        (await fired.Task.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        await Task.Delay(150);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Cancel_BeforeElapse_NeverFires()
    {
        var fired = new TaskCompletionSource<bool>();
        var watchdog = NewWatchdog(TimeSpan.FromMilliseconds(150));
        var matchId = Guid.NewGuid();

        watchdog.Arm(matchId, () => { fired.TrySetResult(true); return Task.CompletedTask; });
        watchdog.Cancel(matchId);

        await Task.Delay(350);
        fired.Task.IsCompleted.Should().BeFalse("a cancelled watchdog must never fire");
    }

    [Fact]
    public void BumpAndCancel_OnUnarmedMatch_AreNoOp()
    {
        var watchdog = NewWatchdog(TimeSpan.FromMilliseconds(100));
        var matchId = Guid.NewGuid();

        var bump = () => watchdog.Bump(matchId);
        var cancel = () => watchdog.Cancel(matchId);

        bump.Should().NotThrow();
        cancel.Should().NotThrow();
    }

    [Fact]
    public async Task Arm_CallbackThrows_IsObservedAndLoggedAtError()
    {
        var logger = new CaptureLogger<MatchEngineWatchdog>();
        var threw = new TaskCompletionSource<bool>();
        var watchdog = NewWatchdog(TimeSpan.FromMilliseconds(50), logger);

        watchdog.Arm(Guid.NewGuid(), () =>
        {
            threw.TrySetResult(true);
            throw new InvalidOperationException("boom in watchdog callback");
        });

        await threw.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error && e.Ex is InvalidOperationException,
            "a faulted onWedged callback must be observed + logged at Error");
    }

    // -----------------------------------------------------------------------
    // Regression: the Cancel-disposes-cts-before-Task.Run-reads-cts.Token race.
    //
    // Bump does Cancel(=Cts.Cancel()+Cts.Dispose()) then Start(=new cts +
    // Task.Run(() => Task.Delay(_noProgress, cts.Token))). On every engine
    // event during autonomous progression Bump is called, so two Bumps can
    // interleave: Bump A schedules its Task.Run but hasn't yet evaluated
    // cts_A.Token when Bump B's Cancel disposes cts_A. Bump A's task then
    // reads cts_A.Token → ObjectDisposedException, which in prod stormed
    // hundreds of times/sec and broke the safety net. The watchdog must never
    // log an ObjectDisposedException under heavy Bump churn — the disposed cts
    // just means a newer timer is already armed, so the racing task is a no-op.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bump_HammeredConcurrently_NeverLogsObjectDisposedException()
    {
        var logger = new CaptureLogger<MatchEngineWatchdog>();
        // Tiny timeout so some timers actually elapse during the churn, widening
        // the window where a racing Task.Run reads a just-disposed cts.Token.
        var watchdog = NewWatchdog(TimeSpan.FromMilliseconds(1), logger);
        var matchId = Guid.NewGuid();

        watchdog.Arm(matchId, () => Task.CompletedTask);

        // Hammer Bump from multiple threads to force the
        // Cancel-disposes-cts-before-Task.Run-reads-Token interleaving.
        const int threads = 8;
        const int iterations = 4000;
        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
                watchdog.Bump(matchId);
        })).ToArray();

        await Task.WhenAll(tasks);
        // Let any in-flight racing Task.Run bodies run their (faulting) Token read.
        await Task.Delay(200);

        watchdog.Cancel(matchId);

        logger.Entries.Should().NotContain(e =>
            e.Ex is ObjectDisposedException,
            "a racing Bump that disposed the cts must not surface as a logged "
            + "ObjectDisposedException — a newer timer is already armed");
    }

    [Fact]
    public async Task Bump_RapidlyThenLeftAlone_FiresExactlyOnce()
    {
        var calls = 0;
        var fired = new TaskCompletionSource<bool>();
        // Mirrors the real call shape: the autonomous game-loop Bumps a single
        // match sequentially on every engine event, far faster than the
        // no-progress window. While Bumps keep coming nothing fires; once the
        // loop stops, exactly one onWedged firing. Timeout sits comfortably
        // above the (single-threaded) churn duration.
        var watchdog = NewWatchdog(TimeSpan.FromMilliseconds(250));
        var matchId = Guid.NewGuid();

        watchdog.Arm(matchId, () =>
        {
            Interlocked.Increment(ref calls);
            fired.TrySetResult(true);
            return Task.CompletedTask;
        });

        // Tight single-threaded Bump loop — this is the production call shape,
        // and also forces the Cancel-dispose/Start-rearm churn whose token-read
        // race the fix closes.
        for (var i = 0; i < 20_000; i++)
            watchdog.Bump(matchId);
        calls.Should().Be(0, "continuous bumps faster than the timeout keep resetting the timer");

        // Left alone past the timeout → exactly one onWedged firing.
        (await fired.Task.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        await Task.Delay(250);
        calls.Should().Be(1, "after the bumps stop the timer must fire exactly once");
    }
}
