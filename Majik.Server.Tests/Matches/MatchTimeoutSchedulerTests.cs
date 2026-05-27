using FluentAssertions;
using Majik.Server.Matches;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchTimeoutSchedulerTests
{
    /// <summary>Captures logged entries so a test can assert a faulted
    /// fire-and-forget callback was observed + structured-logged (Slice 4a #3)
    /// rather than escaping as an UnobservedTaskException.</summary>
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

    [Fact]
    public async Task Schedule_FiresAfterRemainingElapses()
    {
        var fired = new TaskCompletionSource<(Guid, string)>();
        var scheduler = new MatchTimeoutScheduler((matchId, holder, ct) =>
        {
            fired.TrySetResult((matchId, holder));
            return Task.CompletedTask;
        });
        var matchId = Guid.NewGuid();

        scheduler.Schedule(matchId, "stub-alice", 80);
        var got = await fired.Task.WaitAsync(TimeSpan.FromSeconds(2));

        got.Item1.Should().Be(matchId);
        got.Item2.Should().Be("stub-alice");
    }

    [Fact]
    public async Task Schedule_TwiceCancelsFirst()
    {
        var fired = new List<string>();
        var scheduler = new MatchTimeoutScheduler((id, holder, ct) =>
        {
            lock (fired) fired.Add(holder);
            return Task.CompletedTask;
        });
        var matchId = Guid.NewGuid();

        scheduler.Schedule(matchId, "stub-alice", 100);
        scheduler.Schedule(matchId, "stub-bob", 100);
        await Task.Delay(400);

        fired.Should().ContainSingle().Which.Should().Be("stub-bob");
    }

    [Fact]
    public async Task Cancel_StopsCallback()
    {
        var fired = false;
        var scheduler = new MatchTimeoutScheduler((id, holder, ct) =>
        {
            fired = true;
            return Task.CompletedTask;
        });
        var matchId = Guid.NewGuid();

        scheduler.Schedule(matchId, "stub-alice", 100);
        scheduler.Cancel(matchId);
        await Task.Delay(300);

        fired.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // #3 — a faulting timeout callback must be observed + structured-logged,
    // never left to surface as an UnobservedTaskException.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Schedule_CallbackThrows_IsObservedAndLoggedAtError()
    {
        var logger = new CaptureLogger<MatchTimeoutScheduler>();
        var threw = new TaskCompletionSource<bool>();
        var scheduler = new MatchTimeoutScheduler((id, holder, ct) =>
        {
            threw.TrySetResult(true);
            throw new InvalidOperationException("boom in timeout callback");
        }, logger);

        scheduler.Schedule(Guid.NewGuid(), "stub-alice", 50);
        await threw.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Give the catch/log continuation a moment to run after the throw.
        await Task.Delay(100);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error && e.Ex is InvalidOperationException,
            "a faulted fire-and-forget timeout callback must be observed + logged at Error");
    }
}
