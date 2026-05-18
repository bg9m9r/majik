using Majik.Core.CardData.Database;
using Microsoft.EntityFrameworkCore;

namespace Majik.Core.CardData;

/// <summary>
/// EF Core implementation backed by <see cref="CardDbContext"/>. Returns the
/// first printing for a given card name (Scryfall has one row per printing;
/// gameplay only needs the gameplay-relevant fields, which are identical
/// across printings of the same card).
/// </summary>
public sealed class DbCardRepository : ICardRepository
{
    private readonly CardDbContext _db;

    public DbCardRepository(CardDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public CardEntity? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _db.Cards.AsNoTracking().FirstOrDefault(c => c.Name == name);
    }
}
