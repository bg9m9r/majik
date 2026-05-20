using FluentAssertions;
using Majik.Server.Decks;
using Majik.Server.Tests.Profiles;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Decks;

public class DeckRepositoryTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public DeckRepositoryTests(TestMongoFixture fixture) => _fixture = fixture;

    private async Task<DeckRepository> NewRepoAsync()
    {
        var repo = new DeckRepository(_fixture.NewDatabase());
        await repo.EnsureIndexesAsync(CancellationToken.None);
        return repo;
    }

    private static Deck NewDeck(string ownerSub, string name) => new()
    {
        Id = Guid.NewGuid(),
        OwnerSub = ownerSub,
        Name = name,
        Mainboard = new List<DeckCardEntry>
        {
            new() { Name = "Forest", Count = 24 },
            new() { Name = "Grizzly Bears", Count = 4 },
        },
        Sideboard = new List<DeckCardEntry>(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task InsertAndGet_RoundTrips()
    {
        var repo = await NewRepoAsync();
        var d = NewDeck("stub-alice", "Mono Green");
        await repo.InsertAsync(d, CancellationToken.None);

        var fetched = await repo.GetByIdForOwnerAsync(d.Id, "stub-alice", CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Mainboard.Should().HaveCount(2);
        fetched.Sideboard.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdForOwner_OtherOwnerReturnsNull()
    {
        var repo = await NewRepoAsync();
        var d = NewDeck("stub-alice", "Mono Green");
        await repo.InsertAsync(d, CancellationToken.None);

        var fetched = await repo.GetByIdForOwnerAsync(d.Id, "stub-bob", CancellationToken.None);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task ListByOwner_ReturnsOnlyOwned_SortedByUpdatedAtDesc()
    {
        var repo = await NewRepoAsync();
        var older = NewDeck("stub-alice", "Burn");
        older.UpdatedAt = DateTime.UtcNow.AddMinutes(-10);
        var newer = NewDeck("stub-alice", "Stompy");
        newer.UpdatedAt = DateTime.UtcNow;
        var bobs = NewDeck("stub-bob", "Control");
        await repo.InsertAsync(older, CancellationToken.None);
        await repo.InsertAsync(newer, CancellationToken.None);
        await repo.InsertAsync(bobs, CancellationToken.None);

        var found = await repo.ListByOwnerAsync("stub-alice", CancellationToken.None);

        found.Select(d => d.Name).Should().ContainInOrder("Stompy", "Burn");
    }

    [Fact]
    public async Task Insert_DuplicateNameForSameOwner_Throws()
    {
        var repo = await NewRepoAsync();
        await repo.InsertAsync(NewDeck("stub-alice", "Burn"), CancellationToken.None);

        var act = () => repo.InsertAsync(NewDeck("stub-alice", "Burn"), CancellationToken.None);

        await act.Should().ThrowAsync<MongoWriteException>();
    }

    [Fact]
    public async Task Insert_SameNameDifferentOwner_Allowed()
    {
        var repo = await NewRepoAsync();
        await repo.InsertAsync(NewDeck("stub-alice", "Burn"), CancellationToken.None);
        await repo.InsertAsync(NewDeck("stub-bob", "Burn"), CancellationToken.None);

        var aliceCount = await repo.CountByOwnerAsync("stub-alice", CancellationToken.None);
        var bobCount = await repo.CountByOwnerAsync("stub-bob", CancellationToken.None);

        aliceCount.Should().Be(1);
        bobCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateForOwner_OwnerMatch_Persists()
    {
        var repo = await NewRepoAsync();
        var d = NewDeck("stub-alice", "Burn");
        await repo.InsertAsync(d, CancellationToken.None);

        var newMainboard = new List<DeckCardEntry>
        {
            new() { Name = "Mountain", Count = 24 },
            new() { Name = "Lightning Bolt", Count = 4 },
        };
        var ok = await repo.UpdateForOwnerAsync(d.Id, "stub-alice",
            newName: "Burn 2.0", newMainboard, new List<DeckCardEntry>(),
            now: DateTime.UtcNow, CancellationToken.None);

        ok.Should().BeTrue();
        var fetched = await repo.GetByIdForOwnerAsync(d.Id, "stub-alice", CancellationToken.None);
        fetched!.Name.Should().Be("Burn 2.0");
        fetched.Mainboard.Should().HaveCount(2);
        fetched.Mainboard[0].Name.Should().Be("Mountain");
    }

    [Fact]
    public async Task UpdateForOwner_OwnerMismatch_NoOp()
    {
        var repo = await NewRepoAsync();
        var d = NewDeck("stub-alice", "Burn");
        await repo.InsertAsync(d, CancellationToken.None);

        var ok = await repo.UpdateForOwnerAsync(d.Id, "stub-bob",
            newName: "Stolen", new List<DeckCardEntry>(), new List<DeckCardEntry>(),
            now: DateTime.UtcNow, CancellationToken.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteForOwner_OwnerMatch_RemovesRow()
    {
        var repo = await NewRepoAsync();
        var d = NewDeck("stub-alice", "Burn");
        await repo.InsertAsync(d, CancellationToken.None);

        var deleted = await repo.DeleteForOwnerAsync(d.Id, "stub-alice", CancellationToken.None);

        deleted.Should().Be(1);
        (await repo.GetByIdForOwnerAsync(d.Id, "stub-alice", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteForOwner_OwnerMismatch_Zero()
    {
        var repo = await NewRepoAsync();
        var d = NewDeck("stub-alice", "Burn");
        await repo.InsertAsync(d, CancellationToken.None);

        var deleted = await repo.DeleteForOwnerAsync(d.Id, "stub-bob", CancellationToken.None);

        deleted.Should().Be(0);
    }
}
