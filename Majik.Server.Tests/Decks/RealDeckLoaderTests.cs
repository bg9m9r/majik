using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Decks;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Decks;

public class RealDeckLoaderTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public RealDeckLoaderTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class FakeCardRepo : ICardRepository
    {
        public Dictionary<string, CardEntity> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Implemented { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CardEntity? GetByName(string name) => Cards.TryGetValue(name, out var c) ? c : null;
        public IReadOnlyList<CardEntity> Search(string? q, bool io, int l,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
            => throw new NotImplementedException();
        public bool IsImplemented(string name) => Implemented.Contains(name);
        public void SetImplemented(string n, bool v) => throw new NotImplementedException();

        public void Add(string name, string typeLine)
        {
            Cards[name] = new CardEntity
            {
                Name = name, ScryfallId = Guid.NewGuid().ToString(), ManaCost = "",
                TypeLine = typeLine, Set = "TST", CollectorNumber = "1", IsImplemented = true,
            };
            Implemented.Add(name);
        }
    }

    private async Task<(RealDeckLoader loader, DeckRepository repo)> NewLoaderAsync()
    {
        var repo = new DeckRepository(_fixture.NewDatabase());
        await repo.EnsureIndexesAsync(CancellationToken.None);
        var cards = new FakeCardRepo();
        cards.Add("Forest", "Basic Land — Forest");
        cards.Add("Mountain", "Basic Land — Mountain");
        cards.Add("Grizzly Bears", "Creature — Bear");
        cards.Add("Hill Giant", "Creature — Giant");
        var validator = new DeckValidationService(cards);
        return (new RealDeckLoader(repo, cards, validator), repo);
    }

    private static Deck ValidDeck(Guid id, string ownerSub) => new()
    {
        Id = id,
        OwnerSub = ownerSub,
        Name = "Test",
        Mainboard = new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 24 },
            new() { Name = "Grizzly Bears", Count = 4 },
            new() { Name = "Hill Giant", Count = 4 },
            new() { Name = "Mountain", Count = 28 },
        },
        Sideboard = new List<DeckCardEntry>(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task LoadAsync_ValidDeck_ReturnsSixtyCardInstances()
    {
        var (loader, repo) = await NewLoaderAsync();
        var id = Guid.NewGuid();
        await repo.InsertAsync(ValidDeck(id, "stub-alice"), CancellationToken.None);

        var cards = await loader.LoadAsync(id.ToString(), CancellationToken.None);

        cards.Should().HaveCount(60);
    }

    [Fact]
    public async Task LoadAsync_FourLightningBolts_ProducesFourDistinctInstances()
    {
        var (loader, repo) = await NewLoaderAsync();
        var id = Guid.NewGuid();
        // override the deck inline to contain 4 Hill Giants
        var deck = ValidDeck(id, "stub-alice");
        await repo.InsertAsync(deck, CancellationToken.None);

        var cards = await loader.LoadAsync(id.ToString(), CancellationToken.None);

        var hillGiants = cards.Where(c => c.Name == "Hill Giant").ToList();
        hillGiants.Should().HaveCount(4);
        hillGiants.Select(c => c).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task LoadAsync_BadGuid_Throws()
    {
        var (loader, _) = await NewLoaderAsync();

        var act = () => loader.LoadAsync("not-a-guid", CancellationToken.None);

        await act.Should().ThrowAsync<DeckLoadException>();
    }

    [Fact]
    public async Task LoadAsync_DeckNotFound_Throws()
    {
        var (loader, _) = await NewLoaderAsync();

        var act = () => loader.LoadAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        await act.Should().ThrowAsync<DeckLoadException>();
    }

    [Fact]
    public async Task LoadAsync_InvalidDeck_ThrowsWithValidationErrors()
    {
        var (loader, repo) = await NewLoaderAsync();
        var id = Guid.NewGuid();
        var undersized = ValidDeck(id, "stub-alice");
        undersized.Mainboard = new List<DeckCardEntry> { new() { Name = "Forest", Count = 30 } };
        await repo.InsertAsync(undersized, CancellationToken.None);

        var act = () => loader.LoadAsync(id.ToString(), CancellationToken.None);

        (await act.Should().ThrowAsync<DeckLoadException>())
            .WithMessage("*minimum 60*");
    }
}
