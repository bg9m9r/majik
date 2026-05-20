using System.Collections.Concurrent;

namespace Majik.Server.Matches;

/// <summary>Per-match Timer firing the timeout callback when the priority
/// holder's clock should hit 0. Calls to <see cref="Schedule"/> replace any
/// previously-scheduled timer for the same matchId. The callback receives
/// the matchId + holder sub.</summary>
public sealed class MatchTimeoutScheduler
{
    private readonly Func<Guid, string, CancellationToken, Task> _onTimeout;
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public MatchTimeoutScheduler(Func<Guid, string, CancellationToken, Task> onTimeout)
    {
        _onTimeout = onTimeout;
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
            catch (TaskCanceledException) { }
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
