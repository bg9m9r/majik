using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Hubs;

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

    public GameHubBridge(GameFacade facade, IHubContext<GameHub> hub)
    {
        ArgumentNullException.ThrowIfNull(facade);
        ArgumentNullException.ThrowIfNull(hub);

        var group = GameHub.GroupName(facade.GameId);
        _eventSubscription = facade.Subscribe(evt => Forward(hub, group, "event", evt));
        _promptSubscription = facade.SubscribePrompts(p => Forward(hub, group, "prompt", p));
    }

    private static void Forward(IHubContext<GameHub> hub, string group, string method, object payload)
    {
        // Fire-and-forget — Subscribe handlers are sync, hub send is
        // async; observe the task so exceptions surface in logs but
        // don't block the engine loop.
        _ = hub.Clients.Group(group).SendAsync(method, payload);
    }

    public void Dispose()
    {
        _eventSubscription.Dispose();
        _promptSubscription.Dispose();
    }
}
