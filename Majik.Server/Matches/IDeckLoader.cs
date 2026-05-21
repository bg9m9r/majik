using Majik.Core.Cards;

namespace Majik.Server.Matches;

public interface IDeckLoader
{
    Task<IReadOnlyList<ICard>> LoadAsync(string deckId, CancellationToken ct);

    /// <summary>Materializes a deck directly from a list of card names,
    /// bypassing the deck database. Used by the bot-opponent flow where the
    /// "deck" is a static archetype list, not a user-owned <c>Deck</c>
    /// document. Each name produces a fresh <see cref="ICard"/> instance —
    /// duplicates in the input produce distinct game objects.</summary>
    Task<IReadOnlyList<ICard>> LoadFromCardNamesAsync(IReadOnlyList<string> cardNames, CancellationToken ct);
}
