using Majik.Core.CardData.Database;

namespace Majik.Core.CardData;

/// <summary>
/// Card-data lookup abstraction. Production: <see cref="DbCardRepository"/>
/// against the Scryfall SQLite DB. Tests: in-memory implementations.
/// </summary>
public interface ICardRepository
{
    CardEntity? GetByName(string name);
}
