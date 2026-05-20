using Majik.Core.Cards;

namespace Majik.Server.Matches;

/// <summary>
/// Test/stub <see cref="IDeckLoader"/> that returns 60 placeholder cards for
/// any deckId. Used in unit tests and integration scaffolding; real snapshot
/// resolution is wired in T9 via <see cref="Majik.Server.Decks.RealDeckLoader"/>.
/// </summary>
public sealed class StubDeckLoader : IDeckLoader
{
    public Task<IReadOnlyList<ICard>> LoadAsync(string deckId, CancellationToken ct)
    {
        IReadOnlyList<ICard> deck = Enumerable.Range(1, 60)
            .Select(_ => (ICard)new Card("Stub Card"))
            .ToList();
        return Task.FromResult(deck);
    }
}
