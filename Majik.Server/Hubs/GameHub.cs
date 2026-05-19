using Majik.Core.Api;
using Majik.Server.Composition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Hubs;

/// <summary>
/// SignalR hub for live game updates. Clients call <see cref="JoinGame"/>
/// after connecting; the hub adds the connection to the per-game group
/// and the <see cref="GameHubBridge"/> publishes events into that group
/// as the engine emits them.
///
/// Auth: AsPlayer at the hub level — every connection must carry a
/// valid bearer token. Per-player visibility filtering (so a client
/// doesn't see the opponent's hand) is enforced by the
/// StateSnapshotter projection in a later slice; the hub bridges
/// already-filtered events.
/// </summary>
[Authorize(Policy = AuthRegistration.AsPlayerPolicy)]
public sealed class GameHub : Hub
{
    private readonly GameRegistry _registry;

    public GameHub(GameRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Subscribe this connection to a game's event stream.
    /// Throws <see cref="HubException"/> if the game doesn't exist.</summary>
    public async Task JoinGame(Guid gameId)
    {
        if (_registry.Get(gameId) == null)
        {
            throw new HubException($"Game {gameId} not found");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(gameId));
    }

    /// <summary>Remove this connection from a game's event stream.</summary>
    public Task LeaveGame(Guid gameId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(gameId));

    /// <summary>Group name for a given game id. Single source of truth
    /// so the bridge and the hub stay in sync.</summary>
    internal static string GroupName(Guid gameId) => $"game:{gameId}";
}
