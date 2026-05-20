using Majik.Core.Cards;
using Majik.Server.Matches;

namespace Majik.Server.Tests.Helpers;

/// <summary>
/// Test-only <see cref="IDeckLoader"/> that returns 60 placeholder cards for
/// any deckId. Used by MatchService tests that don't exercise real deck loading.
/// Production wiring uses <see cref="Majik.Server.Decks.RealDeckLoader"/>.
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
