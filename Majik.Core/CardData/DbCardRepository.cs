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

        // Exact match first.
        var exact = _db.Cards.AsNoTracking().FirstOrDefault(c => c.Name == name);
        if (exact != null) return exact;

        // Double-faced cards (CR 712) are stored as "Front // Back" in
        // Scryfall. A decklist normally references only the front face;
        // match the prefix when the exact lookup fails.
        var prefix = name + " // ";
        return _db.Cards.AsNoTracking()
            .FirstOrDefault(c => c.Name.StartsWith(prefix));
    }

    public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit)
    {
        var query = _db.Cards.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            query = query.Where(c => EF.Functions.Like(c.Name, $"%{needle}%"));
        }
        if (implementedOnly)
        {
            query = query.Where(c => c.IsImplemented);
        }
        return query.OrderBy(c => c.Name).Take(limit).ToList();
    }

    public bool IsImplemented(string name)
    {
        var card = _db.Cards.AsNoTracking().FirstOrDefault(c => c.Name == name);
        return card?.IsImplemented ?? false;
    }

    public void SetImplemented(string name, bool value)
    {
        var card = _db.Cards.FirstOrDefault(c => c.Name == name);
        if (card == null)
            throw new ArgumentException($"Card not found: {name}", nameof(name));
        card.IsImplemented = value;
        _db.SaveChanges();
    }
}
