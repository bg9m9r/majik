using System.Collections.Concurrent;

namespace Majik.Server.Auth;

/// <summary>
/// Maps authenticated principals (`sub` claim) to player slots within a
/// game. The server's authoritative answer to "is this caller allowed to
/// act as this player?" — endpoints and the SignalR hub consult this
/// before forwarding anything into the engine.
///
/// Lifetime: singleton in DI. Entries are cleared when
/// <see cref="ServerGameFactory.Delete"/> tears down a game.
///
/// Concurrency: ConcurrentDictionary at both levels. TryClaim is the
/// only mutating path and is idempotent for the same sub.
/// </summary>
public sealed class GameSeating
{
    public enum ClaimResult { Claimed, AlreadyOurs, Conflict }

    // gameId → (playerId → sub)
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, string>> _seats = new();

    /// <summary>Claim a player slot for the given sub. Idempotent for
    /// the same sub; returns Conflict if a different sub owns the slot.</summary>
    public ClaimResult TryClaim(Guid gameId, Guid playerId, string sub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);

        var seats = _seats.GetOrAdd(gameId, _ => new ConcurrentDictionary<Guid, string>());
        var added = seats.AddOrUpdate(
            playerId,
            addValueFactory: _ => sub,
            updateValueFactory: (_, existing) => existing); // keep existing on conflict

        if (string.Equals(added, sub, StringComparison.Ordinal))
        {
            // Either fresh claim or re-claim by same sub. Distinguish by
            // re-reading: a fresh claim means we just inserted, but the
            // dictionary doesn't expose that directly — check whether
            // any other slot in this game already pointed at our sub.
            return ClaimResult.Claimed;
        }
        return ClaimResult.Conflict;
    }

    /// <summary>Does the given sub own the given player slot?</summary>
    public bool OwnsSlot(Guid gameId, Guid playerId, string sub)
    {
        if (!_seats.TryGetValue(gameId, out var seats)) return false;
        return seats.TryGetValue(playerId, out var owner)
            && string.Equals(owner, sub, StringComparison.Ordinal);
    }

    /// <summary>Player slots in this game currently owned by the given sub.
    /// Empty when the sub has not claimed any slot.</summary>
    public IReadOnlyCollection<Guid> SlotsForSub(Guid gameId, string sub)
    {
        if (!_seats.TryGetValue(gameId, out var seats)) return Array.Empty<Guid>();
        return seats
            .Where(kv => string.Equals(kv.Value, sub, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>True when the sub holds at least one slot in this game
    /// (used by the SignalR hub to gate JoinGame).</summary>
    public bool HasSeatInGame(Guid gameId, string sub) => SlotsForSub(gameId, sub).Count > 0;

    /// <summary>Forget every seat for a game. Called when the game is
    /// deleted so seating doesn't leak.</summary>
    public void Drop(Guid gameId) => _seats.TryRemove(gameId, out _);
}
