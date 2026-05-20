using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Tests.Profiles;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchRepositoryTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchRepositoryTests(TestMongoFixture fixture) => _fixture = fixture;

    private async Task<MatchRepository> NewRepoAsync()
    {
        var repo = new MatchRepository(_fixture.NewDatabase());
        await repo.EnsureIndexesAsync(CancellationToken.None);
        return repo;
    }

    private static Match NewMatch(MatchState state = MatchState.Open, MatchVisibility vis = MatchVisibility.Public, string creatorSub = "stub-alice") =>
        new()
        {
            Id = Guid.NewGuid(),
            State = state,
            Visibility = vis,
            Format = "constructed",
            ClockMinutes = 20,
            Creator = new MatchPlayer { Sub = creatorSub, Handle = "Alice", DeckId = "burn", DeckSnapshot = new List<string>() },
            CreatorMillisRemaining = 1_200_000,
            OpponentMillisRemaining = 1_200_000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task InsertAndGet_RoundTrips()
    {
        var repo = await NewRepoAsync();
        var m = NewMatch();
        await repo.InsertAsync(m, CancellationToken.None);

        var fetched = await repo.GetByIdAsync(m.Id, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Creator.Sub.Should().Be("stub-alice");
    }

    [Fact]
    public async Task ListOpenPublic_ReturnsOnlyMatchingMatches()
    {
        var repo = await NewRepoAsync();
        await repo.InsertAsync(NewMatch(MatchState.Open, MatchVisibility.Public, "alice"), CancellationToken.None);
        await repo.InsertAsync(NewMatch(MatchState.Open, MatchVisibility.Invite, "bob"), CancellationToken.None);
        await repo.InsertAsync(NewMatch(MatchState.Playing, MatchVisibility.Public, "carol"), CancellationToken.None);

        var open = await repo.ListOpenPublicAsync(limit: 50, CancellationToken.None);

        open.Should().HaveCount(1);
        open[0].Creator.Sub.Should().Be("alice");
    }

    [Fact]
    public async Task UpdateStateAtomic_OnlyWhenFilterMatches()
    {
        var repo = await NewRepoAsync();
        var m = NewMatch();
        await repo.InsertAsync(m, CancellationToken.None);

        var ok = await repo.TryAtomicUpdateAsync(
            m.Id,
            expectedState: MatchState.Open,
            update: Builders<Match>.Update.Set(x => x.State, MatchState.Joined),
            CancellationToken.None);

        ok.Should().BeTrue();
        (await repo.GetByIdAsync(m.Id, CancellationToken.None))!.State.Should().Be(MatchState.Joined);

        var noOp = await repo.TryAtomicUpdateAsync(
            m.Id,
            expectedState: MatchState.Open,
            update: Builders<Match>.Update.Set(x => x.State, MatchState.Rolling),
            CancellationToken.None);

        noOp.Should().BeFalse();
    }

    [Fact]
    public async Task ListInState_ReturnsAcrossVisibility()
    {
        var repo = await NewRepoAsync();
        await repo.InsertAsync(NewMatch(MatchState.Open, MatchVisibility.Public), CancellationToken.None);
        await repo.InsertAsync(NewMatch(MatchState.Open, MatchVisibility.Invite), CancellationToken.None);

        var open = await repo.ListInStateAsync(MatchState.Open, CancellationToken.None);

        open.Should().HaveCount(2);
    }
}
