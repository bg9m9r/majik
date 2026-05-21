using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for the abstraction itself via an inline impl; the EF-backed
/// <see cref="DbCardRepository"/> is exercised by the live DB integration test.
/// </summary>
public class InMemoryCardRepositoryTests
{
    [Fact]
    public void GetByName_Found_ReturnsEntity()
    {
        var repo = new DictRepo(new Dictionary<string, CardEntity>
        {
            ["Lightning Bolt"] = new CardEntity { Name = "Lightning Bolt", TypeLine = "Instant" },
        });

        repo.GetByName("Lightning Bolt").Should().NotBeNull();
    }

    [Fact]
    public void GetByName_NotFound_ReturnsNull()
    {
        var repo = new DictRepo(new Dictionary<string, CardEntity>());

        repo.GetByName("Nope").Should().BeNull();
    }

    [Fact]
    public void GetByName_NullOrWhitespace_ReturnsNull()
    {
        var repo = new DictRepo(new Dictionary<string, CardEntity>());

        repo.GetByName("").Should().BeNull();
        repo.GetByName("   ").Should().BeNull();
    }

    private sealed class DictRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _by;
        public DictRepo(Dictionary<string, CardEntity> by) { _by = by; }
        public CardEntity? GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
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
