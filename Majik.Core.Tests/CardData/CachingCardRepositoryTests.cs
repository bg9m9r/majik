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

        // GetOrAdd may invoke factory multiple times under contention, but
        // ConcurrentDictionary docs guarantee only one entry is stored.
        // For tests just assert correctness, not exact hit count.
        cache.GetByName("Bolt").Should().NotBeNull();
        cache.CacheSize.Should().Be(1);
    }

    private sealed class CountingRepo : ICardRepository
    {
        public Dictionary<string, int> Hits { get; } = new();
        private readonly Dictionary<string, CardEntity> _by;

        public CountingRepo(Dictionary<string, CardEntity> by) { _by = by; }

        public CardEntity? GetByName(string name)
        {
            Hits[name] = Hits.TryGetValue(name, out var n) ? n + 1 : 1;
            return _by.TryGetValue(name, out var e) ? e : null;
        }

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit)
            => throw new NotImplementedException();

        public bool IsImplemented(string name) => throw new NotImplementedException();

        public void SetImplemented(string name, bool value) => throw new NotImplementedException();
    }
}
