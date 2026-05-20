namespace Majik.Server.Matches;

/// <summary>Stub scheduler. T10 replaces this with real chess-clock enforcement.</summary>
public sealed class MatchTimeoutScheduler
{
    private readonly Action<Guid, string>? _onTimeout;

    /// <param name="onTimeout">Callback invoked when a player's clock expires.
    /// The stub ignores it; T10 wires real cancellation tokens.</param>
    public MatchTimeoutScheduler(Action<Guid, string>? onTimeout = null)
    {
        _onTimeout = onTimeout;
    }

    /// <summary>Schedule a timeout for the given match / player sub in <paramref name="millisRemaining"/> ms.
    /// No-op in this stub.</summary>
    public void Schedule(Guid matchId, string playerSub, long millisRemaining) { }

    /// <summary>Cancel any pending timeout for the given match.
    /// No-op in this stub.</summary>
    public void Cancel(Guid matchId) { }
}
