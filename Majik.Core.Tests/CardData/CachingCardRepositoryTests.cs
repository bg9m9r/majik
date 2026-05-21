using System.Collections.Concurrent;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Xunit;

public class CachingCardRepositoryTests
{
    [Fact]
    public void GetByName_Cached_OnlyHitsInnerOnce()
    {
        var inner = new CountingRepo(new Dictionary<string, CardEntity>
        {
            ["Bolt"] = new CardEntity { Name = "Bolt", TypeLine = "Instant" },
        });
        var cache = new CachingCardRepository(inner);

        cache.GetByName("Bolt").Should().NotBeNull();
        cache.GetByName("Bolt").Should().NotBeNull();
        cache.GetByName("Bolt").Should().NotBeNull();

        inner.Hits["Bolt"].Should().Be(1);
    }

    [Fact]
    public void GetByName_Miss_AlsoCached()
    {
        var inner = new CountingRepo(new Dictionary<string, CardEntity>());
        var cache = new CachingCardRepository(inner);

        cache.GetByName("Nope").Should().BeNull();
        cache.GetByName("Nope").Should().BeNull();

        inner.Hits["Nope"].Should().Be(1);
    }

    [Fact]
    public void Concurrent_Lookup_OnlyOneInnerHitPerName()
    {
        var inner = new CountingRepo(new Dictionary<string, CardEntity>
        {
            ["Bolt"] = new CardEntity { Name = "Bolt", TypeLine = "Instant" },
        });
        var cache = new CachingCardRepository(inner);

        Enumerable.Range(0, 100)
            .AsParallel()
            .ForAll(_ => cache.GetByName("Bolt"));

        // Lazy<T> with ExecutionAndPublication guarantees the inner factory
        // runs exactly once even when many threads race on the same key.
        cache.GetByName("Bolt").Should().NotBeNull();
        cache.CacheSize.Should().Be(1);
        inner.Hits["Bolt"].Should().Be(1);
    }

    private sealed class CountingRepo : ICardRepository
    {
        // ConcurrentDictionary because Concurrent_Lookup_OnlyOneInnerHitPerName
        // fires 100 PLINQ calls in parallel. A plain Dictionary mutated from
        // multiple threads corrupts and throws "non-concurrent collection"
        // errors that mask the actual assertion.
        public ConcurrentDictionary<string, int> Hits { get; } = new();
        private readonly Dictionary<string, CardEntity> _by;

        public CountingRepo(Dictionary<string, CardEntity> by) { _by = by; }

        public CardEntity? GetByName(string name)
        {
            Hits.AddOrUpdate(name, 1, (_, n) => n + 1);
            return _by.TryGetValue(name, out var e) ? e : null;
        }

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
            => throw new NotImplementedException();

        public bool IsImplemented(string name) => throw new NotImplementedException();

        public void SetImplemented(string name, bool value) => throw new NotImplementedException();
    }
}
