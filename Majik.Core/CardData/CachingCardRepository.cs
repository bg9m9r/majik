using System.Collections.Concurrent;
using Majik.Core.Cards;
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
    // Lazy<T> values, not raw CardEntity? — ConcurrentDictionary.GetOrAdd
    // is documented to invoke the value factory multiple times under
    // contention. Wrapping in Lazy<T> with ExecutionAndPublication mode
    // guarantees _inner.GetByName is called exactly once per name even
    // when many threads race on the same key. Important for cold-cache
    // bursts (e.g. validator pre-fetching a deck's 60 names in parallel).
    private readonly ConcurrentDictionary<string, Lazy<CardEntity?>> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<BotIntent>> _intentCache = new();

    public CachingCardRepository(ICardRepository inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public CardEntity? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _cache.GetOrAdd(name, n => new Lazy<CardEntity?>(
            () => _inner.GetByName(n),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
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
        // subsequent single-name GetByName calls are served from cache. Wrap
        // each entry in an already-materialized Lazy so the shape matches the
        // GetByName path.
        var results = _inner.GetByNames(names);
        foreach (var card in results)
        {
            var slot = new Lazy<CardEntity?>(() => card, LazyThreadSafetyMode.PublicationOnly);
            _ = slot.Value; // force materialization so .Value never re-runs
            _cache.TryAdd(card.Name, slot);
        }
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

    public BotIntent IntentFor(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return BotIntent.None;
        return _intentCache.GetOrAdd(cardName, n => new Lazy<BotIntent>(
            () => _inner.IntentFor(n),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>For tests/diagnostics: number of distinct keys cached.</summary>
    public int CacheSize => _cache.Count;
}
