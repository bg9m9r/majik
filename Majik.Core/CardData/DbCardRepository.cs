using Majik.Core.CardData.Database;
using Microsoft.EntityFrameworkCore;

namespace Majik.Core.CardData;

/// <summary>
/// EF Core implementation backed by <see cref="CardDbContext"/>. Returns the
/// first printing for a given card name (Scryfall has one row per printing;
/// gameplay only needs the gameplay-relevant fields, which are identical
/// across printings of the same card).
///
/// Thread-safety: this class is safe to register as a singleton when
/// constructed with a context factory. Each public method opens and (when
/// the factory owns it) disposes its own <see cref="CardDbContext"/>, so
/// concurrent callers don't share a context (EF Core's <c>DbContext</c> is
/// not thread-safe — see EF Core docs).
/// </summary>
public sealed class DbCardRepository : ICardRepository
{
    private readonly Func<CardDbContext> _contextFactory;
    private readonly bool _ownsContext;

    /// <summary>
    /// Preferred constructor — takes a factory delegate. Each method opens
    /// a fresh context and disposes it afterward, so the repository is
    /// safe to register as a singleton.
    /// </summary>
    public DbCardRepository(Func<CardDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _ownsContext = true;
    }

    /// <summary>
    /// Legacy constructor — takes a single shared context. Used by test
    /// fixtures that build their own context inline. Methods do NOT dispose
    /// the context in this mode; the caller owns its lifetime.
    /// </summary>
    public DbCardRepository(CardDbContext db)
    {
        if (db == null) throw new ArgumentNullException(nameof(db));
        _contextFactory = () => db;
        _ownsContext = false;
    }

    private void Dispose(CardDbContext db)
    {
        if (_ownsContext) db.Dispose();
    }

    public CardEntity? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var db = _contextFactory();
        try
        {
            // Scryfall stores one row per printing. Prefer the implemented
            // representative if any exists; otherwise fall back to any row.
            var exact = db.Cards.AsNoTracking()
                .Where(c => c.Name == name)
                .OrderByDescending(c => c.IsImplemented)
                .FirstOrDefault();
            if (exact != null) return exact;

            // Double-faced cards (CR 712) stored as "Front // Back" — match prefix.
            var prefix = name + " // ";
            return db.Cards.AsNoTracking()
                .Where(c => c.Name.StartsWith(prefix))
                .OrderByDescending(c => c.IsImplemented)
                .FirstOrDefault();
        }
        finally
        {
            Dispose(db);
        }
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
        var fetchLimit = hasFilters ? Math.Max(limit * 10, 500) : limit;

        var db = _contextFactory();
        try
        {
            IQueryable<CardEntity> query = db.Cards.AsNoTracking();

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
        finally
        {
            Dispose(db);
        }
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

        if (filter.Contains("C") && cardColors.Count == 0) return true;
        return cardColors.Any(cc => filter.Contains(cc, StringComparer.OrdinalIgnoreCase));
    }

    private static bool MatchesTypes(CardEntity c, HashSet<string> filter)
    {
        var typeLine = c.TypeLine ?? "";
        var typePart = typeLine.Split(" — ")[0];
        var typeTokens = typePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return typeTokens.Any(t => filter.Contains(t));
    }

    public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names)
    {
        if (names == null) return Array.Empty<CardEntity>();
        var set = names.Distinct().ToList();
        if (set.Count == 0) return Array.Empty<CardEntity>();

        var db = _contextFactory();
        try
        {
            // EF translates to `WHERE Name IN (...)` which uses IX_Cards_Name index.
            // Scryfall stores one row per printing — "Forest" has ~3900 rows. The
            // IsImplemented flag is set on a single representative row per name
            // (the canonical one). Return one row per name, preferring the
            // implemented printing so downstream consumers see the correct flag.
            var rows = db.Cards.AsNoTracking()
                .Where(c => set.Contains(c.Name))
                .ToList();

            return rows
                .GroupBy(c => c.Name)
                .Select(g => g.OrderByDescending(c => c.IsImplemented).First())
                .ToList();
        }
        finally
        {
            Dispose(db);
        }
    }

    public bool IsImplemented(string name)
    {
        var db = _contextFactory();
        try
        {
            var card = db.Cards.AsNoTracking().FirstOrDefault(c => c.Name == name);
            return card?.IsImplemented ?? false;
        }
        finally
        {
            Dispose(db);
        }
    }

    public void SetImplemented(string name, bool value)
    {
        var db = _contextFactory();
        try
        {
            var card = db.Cards.FirstOrDefault(c => c.Name == name);
            if (card == null)
                throw new ArgumentException($"Card not found: {name}", nameof(name));
            card.IsImplemented = value;
            db.SaveChanges();
        }
        finally
        {
            Dispose(db);
        }
    }
}
