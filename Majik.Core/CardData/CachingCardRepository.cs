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

    /// <summary>For tests/diagnostics: number of distinct keys cached.</summary>
    public int CacheSize => _cache.Count;
}
