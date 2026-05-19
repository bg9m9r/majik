using System.Collections.Concurrent;
using Majik.Core.Api;
using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Hubs;

/// <summary>
/// Owns the lifetime of <see cref="GameHubBridge"/> instances. The game
/// factory calls <see cref="Attach"/> after creating a facade; the
/// registry stores the bridge until <see cref="Detach"/> is invoked
/// (game deleted) so the bus subscription stays alive for the game's
/// life.
/// </summary>
public sealed class GameHubBridgeRegistry
{
    private readonly IHubContext<GameHub> _hub;
    private readonly ConcurrentDictionary<Guid, GameHubBridge> _bridges = new();

    public GameHubBridgeRegistry(IHubContext<GameHub> hub)
    {
        _hub = hub;
    }

    public void Attach(GameFacade facade)
    {
        var bridge = new GameHubBridge(facade, _hub);
        if (!_bridges.TryAdd(facade.GameId, bridge))
        {
            bridge.Dispose();
            throw new InvalidOperationException(
                $"Bridge already attached for game {facade.GameId}");
        }
    }

    public void Detach(Guid gameId)
    {
        if (_bridges.TryRemove(gameId, out var bridge))
        {
            bridge.Dispose();
        }
    }
}
