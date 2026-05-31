using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchServiceCreateTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceCreateTests(TestMongoFixture fixture) => _fixture = fixture;

    private async Task<MatchService> NewServiceAsync()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        await profileRepo.UpsertAsync(new UserProfile
        {
            Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);

        return new MatchService(
            matchRepo,
            profileRepo,
            new DiceRoller(new SystemRandomSource()),
            new StubDeckLoader(),
            new SystemClock(),
            hub: null,
            timeoutScheduler: null,
            gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy());
    }

    [Fact]
    public async Task CreateAsync_PopulatesFromCallerProfileAndRequest()
    {
        var svc = await NewServiceAsync();

        var result = await svc.CreateAsync(
            callerSub: "stub-alice",
            request: new CreateMatchRequest("constructed", "public", "burn", ClockMinutes: 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.State.Should().Be("Open");
        dto.Visibility.Should().Be("Public");
        dto.Format.Should().Be("constructed");
        dto.ClockMinutes.Should().Be(25);
        dto.Creator.Sub.Should().Be("stub-alice");
        dto.Creator.Handle.Should().Be("Alice");
        dto.Creator.DeckId.Should().Be("burn");
        dto.Opponent.Should().BeNull();
        dto.CreatorMillisRemaining.Should().Be(25 * 60_000);
        dto.OpponentMillisRemaining.Should().Be(25 * 60_000);
    }

    [Fact]
    public async Task CreateAsync_DefaultsClockTo20WhenAbsent()
    {
        var svc = await NewServiceAsync();

        var result = await svc.CreateAsync(
            callerSub: "stub-alice",
            request: new CreateMatchRequest("constructed", "public", "burn", ClockMinutes: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClockMinutes.Should().Be(20);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(30)]
    public async Task CreateAsync_AcceptsValidClockMinutes(int minutes)
    {
        var svc = await NewServiceAsync();
        var result = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", minutes),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value!.ClockMinutes.Should().Be(minutes);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(10)]
    [InlineData(45)]
    public async Task CreateAsync_RejectsInvalidClockMinutes(int minutes)
    {
        var svc = await NewServiceAsync();
        var result = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", minutes),
            CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("invalid-clock-minutes");
    }

    [Fact]
    public async Task CreateAsync_BlankDeckId_Returns400()
    {
        var svc = await NewServiceAsync();
        var result = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "", null),
            CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("invalid-request");
    }

    [Fact]
    public async Task CreateAsync_NoProfile_ReturnsNoProfile()
    {
        var svc = await NewServiceAsync();
        var result = await svc.CreateAsync("stub-unknown",
            new CreateMatchRequest("constructed", "public", "burn", null),
            CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("no-profile");
    }

    // PLAN 08 prerequisite — the RNG seed is pinned + persisted at match
    // creation so the game is reproducible from (stored seed, command log).
    [Fact]
    public async Task CreateAsync_PinsAndPersistsAnRngSeed_OnTheMatchDoc()
    {
        var (svc, repo) = await NewServiceWithRepoAsync();

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        created.IsSuccess.Should().BeTrue();

        var persisted = await repo.GetByIdAsync(created.Value!.Id, CancellationToken.None);
        persisted.Should().NotBeNull();
        // A seed was minted + stored (System.Random.Shared.Next() is in
        // [0, int.MaxValue); 0 means "unset" and is astronomically unlikely).
        persisted!.GameSeed.Should().NotBe(0);
    }

    [Fact]
    public async Task CreateAsync_MintsAnIndependentSeedPerMatch()
    {
        var (svc, repo) = await NewServiceWithRepoAsync();

        var a = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "invite", "burn", 20), CancellationToken.None);
        var b = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "invite", "burn", 20), CancellationToken.None);
        a.IsSuccess.Should().BeTrue();
        b.IsSuccess.Should().BeTrue();

        var seedA = (await repo.GetByIdAsync(a.Value!.Id, CancellationToken.None))!.GameSeed;
        var seedB = (await repo.GetByIdAsync(b.Value!.Id, CancellationToken.None))!.GameSeed;
        // Independently minted per match (collisions are astronomically rare).
        seedA.Should().NotBe(seedB);
    }

    private async Task<(MatchService svc, MatchRepository repo)> NewServiceWithRepoAsync()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);
        await profileRepo.UpsertAsync(new UserProfile
        {
            Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }, CancellationToken.None);

        var svc = new MatchService(
            matchRepo, profileRepo,
            new DiceRoller(new SystemRandomSource()),
            new StubDeckLoader(), new SystemClock(),
            hub: null, timeoutScheduler: null, gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy());
        return (svc, matchRepo);
    }
}
