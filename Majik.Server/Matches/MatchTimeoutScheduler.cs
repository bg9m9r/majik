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
        // Capture the token BEFORE publishing the entry, while this cts is still
        // private to this thread, so a racing Cancel/Schedule that disposes it
        // can't make reading cts.Token (here or in the Task.Run body) throw
        // ObjectDisposedException. (Latent here because Schedule is called
        // rarely, but kept consistent with MatchEngineWatchdog.)
        var token = cts.Token;
        var entry = new Entry(holderSub, cts);
        _entries[matchId] = entry;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(due), token);
                _entries.TryRemove(matchId, out _);
                await _onTimeout(matchId, holderSub, CancellationToken.None);
            }
            catch (TaskCanceledException) { /* expected on Cancel/replace */ }
            catch (OperationCanceledException) { /* expected on Cancel/replace */ }
            catch (ObjectDisposedException)
            {
                // A racing Cancel/Schedule disposed the cts before Task.Delay
                // could register on the captured token; a newer timer is already
                // armed (or the match was cancelled), so this stale task is a
                // correct no-op — same as a cancellation, never an error.
            }
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
