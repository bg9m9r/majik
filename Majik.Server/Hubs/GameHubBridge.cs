using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Hubs;

/// <inheritdoc cref="GameHubBridge"/>

/// <summary>
/// Subscribes to a <see cref="GameFacade"/>'s event stream and rebroadcasts
/// every event to the SignalR group that fronts that game. Lifetime is
/// tied to the facade: dispose to stop bridging.
///
/// Slice 4 v1: rebroadcasts every event to every subscriber in the
/// group. Per-player visibility masking (so opponent draws don't leak
/// the card identity) is layered on top in a later slice via a
/// connection-aware projection.
/// </summary>
public sealed class GameHubBridge : IDisposable
{
    private readonly IDisposable _eventSubscription;
    private readonly IDisposable _promptSubscription;

    public GameHubBridge(
        GameFacade facade,
        IHubContext<GameHub> hub,
        HubConnectionRegistry connections)
    {
        ArgumentNullException.ThrowIfNull(facade);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(connections);

        var gameId = facade.GameId;
        var group = GameHub.GroupName(gameId);

        // Events describe public state — broadcast to whole group.
        _eventSubscription = facade.Subscribe(evt =>
            FireAndForget(hub.Clients.Group(group).SendAsync("event", evt)));

        // Prompts are addressed to a specific player slot — push only to
        // connections that own that slot.
        _promptSubscription = facade.SubscribePrompts(p =>
        {
            var targets = connections.ConnectionsForPlayer(gameId, p.PlayerId);
            if (targets.Count == 0) return;
            FireAndForget(hub.Clients.Clients(targets).SendAsync("prompt", p));
        });
    }

    private static void FireAndForget(Task task)
    {
        _ = task; // observed by SignalR's logging pipeline
    }

    public void Dispose()
    {
        _eventSubscription.Dispose();
        _promptSubscription.Dispose();
    }
}
