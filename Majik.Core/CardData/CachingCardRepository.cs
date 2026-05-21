using System.Collections.Concurrent;
using Majik.Core.CardData.Database;

namespace Majik.Core.CardData;

/// <summary>
/// Thread-safe in-memory cache decorator over any <see cref="ICardRepository"/>.
/// Caches both hits (CardEntity) and misses (null) so repeated lookups of
/// the same name are O(1). The cached entity is shared across callers —
/// safe because <see cref="CardEntity"/> instances are treated as immutable
/// once loaded.
/// </summary>
public sealed class CachingCardRepository : ICardRepository
{
    private readonly ICardRepository _inner;
    private readonly ConcurrentDictionary<string, CardEntity?> _cache = new();

    public CachingCardRepository(ICardRepository inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public CardEntity? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _cache.GetOrAdd(name, n => _inner.GetByName(n));
    }

    public IReadOnlyList<CardEntity> Search(
        string? q,
        bool implementedOnly,
        int limit,
        IReadOnlyList<string>? colors = null,
        IReadOnlyList<string>? types = null,
        IReadOnlyList<int>? cmcBuckets = null)
        => _inner.Search(q, implementedOnly, limit, colors, types, cmcBuckets);

    public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names)
    {
        // Delegate to inner; populate per-name cache slots for each hit so that
        // subsequent single-name GetByName calls are served from cache.
        var results = _inner.GetByNames(names);
        foreach (var card in results)
            _cache.TryAdd(card.Name, card);
        return results;
    }

    public bool IsImplemented(string name)
        => _inner.IsImplemented(name);

    public void SetImplemented(string name, bool value)
    {
        _inner.SetImplemented(name, value);
        // Invalidate the cache entry so subsequent GetByName reflects new flag.
        _cache.TryRemove(name, out _);
    }

    /// <summary>For tests/diagnostics: number of distinct keys cached.</summary>
    public int CacheSize => _cache.Count;
}
