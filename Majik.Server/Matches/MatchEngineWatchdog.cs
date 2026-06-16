using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Matches;

/// <summary>Per-match no-progress watchdog. <see cref="Arm"/> begins tracking a
/// match and schedules a timer; if the timeout elapses without an intervening
/// <see cref="Bump"/> or <see cref="Cancel"/>, the supplied <c>onWedged</c>
/// callback is invoked exactly once (the match is considered hung). Every engine
/// event during autonomous progression is expected to <see cref="Bump"/> the
/// timer (= progress), so the callback fires only when the loop makes no
/// progress for the configured window. The watchdog is a dumb timer — it knows
/// nothing about facades; the bridge (W5) supplies the classify-and-report
/// closure. Mirrors <see cref="MatchTimeoutScheduler"/>'s shape (per-match dict
/// + <see cref="CancellationTokenSource"/> + <c>Task.Run</c>/<c>Task.Delay</c>
/// with fault logging and idempotent removal).</summary>
public sealed class MatchEngineWatchdog
{
    private readonly ILogger<MatchEngineWatchdog> _logger;
    private readonly TimeSpan _noProgress;
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    /// <summary>Production constructor. W7 binds <paramref name="noProgressSeconds"/>
    /// from config (default 90).</summary>
    public MatchEngineWatchdog(ILogger<MatchEngineWatchdog> logger, int noProgressSeconds = 90)
        : this(logger, TimeSpan.FromSeconds(noProgressSeconds))
    {
    }

    /// <summary>Internal overload taking the delay directly so tests can inject a
    /// tiny timeout without depending on whole-second granularity.</summary>
    internal MatchEngineWatchdog(ILogger<MatchEngineWatchdog> logger, TimeSpan noProgress)
    {
        _logger = logger;
        _noProgress = noProgress;
    }

    /// <summary>Begin tracking <paramref name="matchId"/> and schedule the timer.
    /// If the timeout elapses with no <see cref="Bump"/>/<see cref="Cancel"/>,
    /// <paramref name="onWedged"/> is invoked exactly once. Arming an
    /// already-armed match replaces the prior timer (cancel old, start new).</summary>
    public void Arm(Guid matchId, Func<Task> onWedged)
    {
        Cancel(matchId);
        Start(matchId, onWedged);
    }

    /// <summary>Reset the timer: cancel the pending delay and re-arm with the
    /// same callback and same delay. No-op if the match isn't currently armed.</summary>
    public void Bump(Guid matchId)
    {
        if (!_entries.TryGetValue(matchId, out var existing)) return;
        var onWedged = existing.OnWedged;
        Cancel(matchId);
        Start(matchId, onWedged);
    }

    /// <summary>Stop tracking + cancel the pending timer. Idempotent.</summary>
    public void Cancel(Guid matchId)
    {
        if (_entries.TryRemove(matchId, out var entry))
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
        }
    }

    private void Start(Guid matchId, Func<Task> onWedged)
    {
        var cts = new CancellationTokenSource();
        var entry = new Entry(onWedged, cts);
        _entries[matchId] = entry;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_noProgress, cts.Token);
                // Remove the entry before firing so a later Bump/Cancel is a
                // clean no-op and the callback runs exactly once.
                _entries.TryRemove(matchId, out _);
                await onWedged();
            }
            catch (TaskCanceledException) { /* expected on Bump/Cancel/Arm-replace */ }
            catch (OperationCanceledException) { /* expected on Bump/Cancel/Arm-replace */ }
            catch (Exception ex)
            {
                // The onWedged callback (classify-and-report) threw. Observe +
                // structured-log so it doesn't surface as an
                // UnobservedTaskException and vanish.
                _logger.LogError(ex,
                    "Match engine watchdog callback faulted. MatchId={MatchId}",
                    matchId);
            }
        });
    }

    private sealed record Entry(Func<Task> OnWedged, CancellationTokenSource Cts);
}
