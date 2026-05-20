using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Server.Decks;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Decks;

public class DeckServiceTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public DeckServiceTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class FakeCardRepo : ICardRepository
    {
        public Dictionary<string, CardEntity> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Implemented { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CardEntity? GetByName(string name) => Cards.TryGetValue(name, out var c) ? c : null;
        public IReadOnlyList<CardEntity> Search(string? q, bool io, int l) => throw new NotImplementedException();
        public bool IsImplemented(string name) => Implemented.Contains(name);
        public void SetImplemented(string name, bool value) => throw new NotImplementedException();

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

    private async Task<(DeckService svc, DeckRepository repo)> NewServiceAsync()
    {
        var repo = new DeckRepository(_fixture.NewDatabase());
        await repo.EnsureIndexesAsync(CancellationToken.None);
        var cards = new FakeCardRepo();
        cards.Add("Forest", "Basic Land — Forest");
        cards.Add("Mountain", "Basic Land — Mountain");
        cards.Add("Grizzly Bears", "Creature — Bear");
        cards.Add("Hill Giant", "Creature — Giant");
        var validator = new DeckValidationService(cards);
        return (new DeckService(repo, validator), repo);
    }

    private static CreateDeckRequest ValidCreate(string name) => new(
        name,
        new[]
        {
            new DeckCardEntryDto("Forest", 24),
            new DeckCardEntryDto("Grizzly Bears", 4),
            new DeckCardEntryDto("Hill Giant", 4),
            new DeckCardEntryDto("Mountain", 28),
        },
        Array.Empty<DeckCardEntryDto>());

    [Fact]
    public async Task CreateAsync_Valid_Returns201()
    {
        var (svc, _) = await NewServiceAsync();

        var r = await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Name.Should().Be("Burn");
        r.Value.OwnerSub.Should().Be("stub-alice");
        r.Value.Mainboard.Should().HaveCount(4);
    }

    [Fact]
    public async Task CreateAsync_CapReached_Returns409()
    {
        var (svc, repo) = await NewServiceAsync();
        for (var i = 0; i < 25; i++)
        {
            await svc.CreateAsync("stub-alice", ValidCreate($"Deck {i}"), CancellationToken.None);
        }

        var r = await svc.CreateAsync("stub-alice", ValidCreate("OneMore"), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("deck-cap-reached");
    }

    [Fact]
    public async Task CreateAsync_NameCollisionSameOwner_Returns409()
    {
        var (svc, _) = await NewServiceAsync();
        await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None);

        var r = await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("name-taken");
    }

    [Fact]
    public async Task CreateAsync_NameCollisionDifferentOwner_Succeeds()
    {
        var (svc, _) = await NewServiceAsync();
        await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None);

        var r = await svc.CreateAsync("stub-bob", ValidCreate("Burn"), CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_InvalidDeck_Returns400WithValidationErrors()
    {
        var (svc, _) = await NewServiceAsync();
        var undersized = new CreateDeckRequest("Tiny",
            new[] { new DeckCardEntryDto("Forest", 48) },
            Array.Empty<DeckCardEntryDto>());

        var r = await svc.CreateAsync("stub-alice", undersized, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("invalid-deck");
        r.Error.Validation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdateAsync_OwnerMatch_Persists()
    {
        var (svc, _) = await NewServiceAsync();
        var created = (await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None)).Value!;

        var update = new UpdateDeckRequest("Burn 2.0", created.Mainboard, created.Sideboard);
        var r = await svc.UpdateAsync("stub-alice", created.Id, update, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Name.Should().Be("Burn 2.0");
    }

    [Fact]
    public async Task UpdateAsync_OtherOwner_Returns404()
    {
        var (svc, _) = await NewServiceAsync();
        var created = (await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None)).Value!;

        var r = await svc.UpdateAsync("stub-bob", created.Id,
            new UpdateDeckRequest("Stolen", created.Mainboard, created.Sideboard),
            CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("deck-not-found");
    }

    [Fact]
    public async Task GetAsync_OtherOwner_Returns404()
    {
        var (svc, _) = await NewServiceAsync();
        var created = (await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None)).Value!;

        var r = await svc.GetAsync("stub-bob", created.Id, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("deck-not-found");
    }

    [Fact]
    public async Task DeleteAsync_OwnerMatch_Succeeds()
    {
        var (svc, _) = await NewServiceAsync();
        var created = (await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None)).Value!;

        var r = await svc.DeleteAsync("stub-alice", created.Id, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_OtherOwner_Returns404()
    {
        var (svc, _) = await NewServiceAsync();
        var created = (await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None)).Value!;

        var r = await svc.DeleteAsync("stub-bob", created.Id, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("deck-not-found");
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyCallersDecks()
    {
        var (svc, _) = await NewServiceAsync();
        await svc.CreateAsync("stub-alice", ValidCreate("Burn"), CancellationToken.None);
        await svc.CreateAsync("stub-alice", ValidCreate("Stompy"), CancellationToken.None);
        await svc.CreateAsync("stub-bob", ValidCreate("Control"), CancellationToken.None);

        var list = await svc.ListAsync("stub-alice", CancellationToken.None);

        list.Should().HaveCount(2);
        list.Select(d => d.Name).Should().BeEquivalentTo(new[] { "Burn", "Stompy" });
    }
}
