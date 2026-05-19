using Majik.Core.Api;
using Majik.Server.Hubs;

namespace Majik.Server.Composition;

/// <summary>
/// Server-side wrapper around <see cref="GameRegistry"/> that also wires
/// the SignalR bridge for every game it creates and tears it down on
/// delete. Endpoints depend on this rather than the bare registry so
/// the bridge lifecycle never desynchronises from the game lifecycle.
/// </summary>
public sealed class ServerGameFactory
{
    private readonly GameRegistry _registry;
    private readonly GameHubBridgeRegistry _bridges;

    public ServerGameFactory(GameRegistry registry, GameHubBridgeRegistry bridges)
    {
        _registry = registry;
        _bridges = bridges;
    }

    public GameFacade Create(string aliceName, string bobName)
    {
        var facade = _registry.Create(aliceName, bobName);
        _bridges.Attach(facade);
        return facade;
    }

    public GameFacade? Get(Guid id) => _registry.Get(id);

    public int Count => _registry.Count;

    public bool Delete(Guid id)
    {
        _bridges.Detach(id);
        return _registry.Remove(id);
    }
}
