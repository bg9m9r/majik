using Majik.Core.Cards;

namespace Majik.Server.Matches;

public interface IDeckLoader
{
    Task<IReadOnlyList<ICard>> LoadAsync(string deckId, CancellationToken ct);
}
