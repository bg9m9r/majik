using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Matches;

/// <summary>Per-match Timer firing the timeout callback when the priority
/// holder's clock should hit 0. Calls to <see cref="Schedule"/> replace any
/// previously-scheduled timer for the same matchId. The callback receives
/// the matchId + holder sub.</summary>
public sealed class MatchTimeoutScheduler
{
    private readonly Func<Guid, string, CancellationToken, Task> _onTimeout;
    private readonly ILogger<MatchTimeoutScheduler>? _logger;
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public MatchTimeoutScheduler(
        Func<Guid, string, CancellationToken, Task> onTimeout,
        ILogger<MatchTimeoutScheduler>? logger = null)
    {
        _onTimeout = onTimeout;
        _logger = logger;
    }

    public void Schedule(Guid matchId, string holderSub, long remainingMillis)
    {
        Cancel(matchId);
        var due = Math.Max(0, remainingMillis);
        var cts = new CancellationTokenSource();
        var entry = new Entry(holderSub, cts);
        _entries[matchId] = entry;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(due), cts.Token);
                _entries.TryRemove(matchId, out _);
                await _onTimeout(matchId, holderSub, CancellationToken.None);
            }
            catch (TaskCanceledException) { /* expected on Cancel/replace */ }
            catch (OperationCanceledException) { /* expected on Cancel/replace */ }
            catch (Exception ex)
            {
                // The timeout callback (OnTimeoutAsync) threw — e.g. the
                // retry policy exhausted its budget on a transient Mongo
                // fault. Observe + structured-log so it doesn't surface as
                // an UnobservedTaskException and vanish; the match is left
                // in Playing and will need manual / cleanup-sweep recovery.
                _logger?.LogError(ex,
                    "Match timeout callback faulted. MatchId={MatchId} HolderSub={HolderSub}",
                    matchId, holderSub);
            }
        });
    }

    public void Cancel(Guid matchId)
    {
        if (_entries.TryRemove(matchId, out var entry))
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
        }
    }

    private sealed record Entry(string HolderSub, CancellationTokenSource Cts);
}
