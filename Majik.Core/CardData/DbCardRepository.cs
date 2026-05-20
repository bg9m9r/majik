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

    public IReadOnlyList<CardEntity> Search(
        string? q,
        bool implementedOnly,
        int limit,
        IReadOnlyList<string>? colors = null,
        IReadOnlyList<string>? types = null,
        IReadOnlyList<int>? cmcBuckets = null)
    {
        var hasFilters = (colors?.Count ?? 0) > 0
                      || (types?.Count ?? 0) > 0
                      || (cmcBuckets?.Count ?? 0) > 0;

        // Over-fetch when in-memory post-filtering is needed so we can still
        // return up to `limit` rows after the filters are applied.
        var fetchLimit = hasFilters ? Math.Max(limit * 10, 500) : limit;

        IQueryable<CardEntity> query = _db.Cards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            query = query.Where(c => EF.Functions.Like(c.Name, $"%{needle}%"));
        }
        if (implementedOnly)
            query = query.Where(c => c.IsImplemented);

        var rows = query.OrderBy(c => c.Name).Take(fetchLimit).ToList();
        if (!hasFilters) return rows;

        IEnumerable<CardEntity> filtered = rows;

        if (colors != null && colors.Count > 0)
        {
            var colorSet = colors.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(c => MatchesColors(c, colorSet));
        }
        if (types != null && types.Count > 0)
        {
            var typeSet = types.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(c => MatchesTypes(c, typeSet));
        }
        if (cmcBuckets != null && cmcBuckets.Count > 0)
        {
            var hasSevenPlus = cmcBuckets.Contains(7);
            var exactBuckets = cmcBuckets.Where(b => b < 7).ToHashSet();
            filtered = filtered.Where(c =>
                c.Cmc.HasValue
                && (exactBuckets.Contains(c.Cmc.Value)
                    || (hasSevenPlus && c.Cmc.Value >= 7)));
        }

        return filtered.Take(limit).ToList();
    }

    private static bool MatchesColors(CardEntity c, HashSet<string> filter)
    {
        List<string>? cardColors;
        try
        {
            cardColors = System.Text.Json.JsonSerializer.Deserialize<List<string>>(c.Colors);
        }
        catch
        {
            cardColors = null;
        }
        cardColors ??= new List<string>();

        // "C" = colorless: card must have zero colors.
        if (filter.Contains("C") && cardColors.Count == 0) return true;
        return cardColors.Any(cc => filter.Contains(cc, StringComparer.OrdinalIgnoreCase));
    }

    private static bool MatchesTypes(CardEntity c, HashSet<string> filter)
    {
        var typeLine = c.TypeLine ?? "";
        // Split on em-dash separator (CR 205.1: "Type — Subtype").
        var typePart = typeLine.Split(" — ")[0];
        var typeTokens = typePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return typeTokens.Any(t => filter.Contains(t));
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
