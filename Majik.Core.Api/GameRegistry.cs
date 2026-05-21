using System.Collections.Concurrent;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Core.Api;

/// <summary>
/// In-memory thread-safe registry of live <see cref="GameFacade"/>s keyed by
/// their <see cref="GameFacade.GameId"/>. A web layer injects this as a
/// singleton.
/// </summary>
public sealed class GameRegistry
{
    private readonly ConcurrentDictionary<Guid, GameFacade> _games = new();

    public GameFacade Create(
        string aliceName,
        string bobName,
        IReadOnlyList<ICard> aliceDeck,
        IReadOnlyList<ICard> bobDeck,
        ICardRepository? cardRepo = null,
        ReplacementBus? replacements = null)
    {
        var facade = GameFacade.Create(aliceName, bobName, aliceDeck, bobDeck, cardRepo, replacements);

        if (!_games.TryAdd(facade.GameId, facade))
        {
            throw new InvalidOperationException(
                $"Guid collision creating game {facade.GameId} — should be impossible.");
        }

        return facade;
    }

    public GameFacade? Get(Guid gameId) => _games.TryGetValue(gameId, out var facade) ? facade : null;

    public bool Remove(Guid gameId) => _games.TryRemove(gameId, out _);

    public int Count => _games.Count;
}
