using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Server.Composition;

/// <summary>
/// Server-side wrapper around <see cref="GameRegistry"/> that delegates
/// game creation and removal. The Match orchestrator now owns the SignalR
/// side via MatchHubPublisher.
/// </summary>
public sealed class ServerGameFactory
{
    private readonly GameRegistry _registry;
    private readonly ICardRepository? _cardRepo;

    public ServerGameFactory(GameRegistry registry, ICardRepository? cardRepo = null)
    {
        _registry = registry;
        _cardRepo = cardRepo;
    }

    public GameFacade Create(string aliceName, string bobName, IReadOnlyList<ICard> aliceDeck, IReadOnlyList<ICard> bobDeck)
    {
        return _registry.Create(aliceName, bobName, aliceDeck, bobDeck, _cardRepo);
    }

    public GameFacade? Get(Guid id) => _registry.Get(id);

    public int Count => _registry.Count;

    public bool Delete(Guid id)
    {
        return _registry.Remove(id);
    }
}
