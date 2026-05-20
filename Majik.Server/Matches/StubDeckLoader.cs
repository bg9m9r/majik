using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Server.Matches;

/// <summary>Returns the same 60-card vanilla deck regardless of input.
/// Sub-project #3 (Deck CRUD) replaces this with the real DeckList-backed loader.</summary>
public sealed class StubDeckLoader : IDeckLoader
{
    public Task<IReadOnlyList<ICard>> LoadAsync(string deckId, CancellationToken ct)
    {
        var cards = new List<ICard>(60);
        for (var i = 0; i < 60; i++)
        {
            cards.Add(new Creature(name: "Vanilla Bear", manaCost: "{1}{G}", power: 2, toughness: 2));
        }
        return Task.FromResult<IReadOnlyList<ICard>>(cards);
    }
}
