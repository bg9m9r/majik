using System.Collections.Concurrent;

namespace Majik.Server.Hubs;

/// <summary>
/// Tracks which player slots a given SignalR connection is acting on
/// behalf of. Populated by <see cref="GameHub.JoinGame"/> using the
/// caller's seat claims; consulted by <see cref="GameHubBridge"/> when
/// routing prompt envelopes so only the right connection sees a
/// "your turn to choose targets" push.
///
/// Game events continue to broadcast to the whole game group — they
/// describe public state that every observer sees.
/// </summary>
public sealed class HubConnectionRegistry
{
    // gameId → (connectionId → owned playerIds)
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, HashSet<Guid>>> _byGame = new();

    public void Register(Guid gameId, string connectionId, IEnumerable<Guid> ownedPlayerIds)
    {
        var inner = _byGame.GetOrAdd(gameId, _ => new ConcurrentDictionary<string, HashSet<Guid>>());
        inner[connectionId] = new HashSet<Guid>(ownedPlayerIds);
    }

    public void Unregister(string connectionId)
    {
        foreach (var inner in _byGame.Values)
        {
            inner.TryRemove(connectionId, out _);
        }
    }

    public void UnregisterFromGame(Guid gameId, string connectionId)
    {
        if (_byGame.TryGetValue(gameId, out var inner))
        {
            inner.TryRemove(connectionId, out _);
        }
    }

    /// <summary>Connection ids in this game whose owned-slots include
    /// <paramref name="playerId"/>.</summary>
    public IReadOnlyList<string> ConnectionsForPlayer(Guid gameId, Guid playerId)
    {
        if (!_byGame.TryGetValue(gameId, out var inner)) return Array.Empty<string>();
        return inner
            .Where(kv => kv.Value.Contains(playerId))
            .Select(kv => kv.Key)
            .ToList();
    }
}
