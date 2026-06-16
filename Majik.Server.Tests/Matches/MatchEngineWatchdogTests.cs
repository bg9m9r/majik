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
}
