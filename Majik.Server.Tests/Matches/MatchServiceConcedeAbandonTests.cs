using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchServiceConcedeAbandonTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceConcedeAbandonTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class CapturePublisher : IMatchHubPublisher
    {
        public List<(Guid matchId, string @event, object payload)> Published { get; } = new();
        public void Publish(Guid matchId, string @event, object payload) =>
            Published.Add((matchId, @event, payload));
    }

    private sealed class StubRandomSource : IRandomSource
    {
        private readonly Queue<int> _values;
        public StubRandomSource(params int[] values) => _values = new Queue<int>(values);
        public int NextInt(int min, int max) => _values.Dequeue();
    }

    /// <summary>
    /// Creates a service + a match already in Playing state (alice wins roll, chooses play).
    /// </summary>
    private async Task<(MatchService svc, CapturePublisher pub, Guid matchId)>
        NewServiceAndPlayingMatchAsync()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-bob", Handle = "bob", HandleDisplay = "Bob", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        var pub = new CapturePublisher();
        // alice wins with 6 vs 2
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource(6, 2)), new StubDeckLoader(), new SystemClock(),
            pub, timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask), gameFactory: null);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        await svc.JoinAsync("stub-bob", matchId, new JoinMatchRequest("stompy"), CancellationToken.None);
        // Per-player roll model: both players must submit rolls before PlayDraw is available
        await svc.SubmitRollAsync("stub-alice", matchId, CancellationToken.None);
        await svc.SubmitRollAsync("stub-bob", matchId, CancellationToken.None);
        await svc.PlayDrawAsync("stub-alice", matchId, new PlayDrawRequest("play"), CancellationToken.None);

        pub.Published.Clear();
        return (svc, pub, matchId);
    }

    /// <summary>
    /// Creates a service + match in Rolling state (not yet Playing).
    /// </summary>
    private async Task<(MatchService svc, Guid matchId)>
        NewServiceAndRollingMatchAsync()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-bob", Handle = "bob", HandleDisplay = "Bob", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource(6, 2)), new StubDeckLoader(), new SystemClock(),
            null, timeoutScheduler: null, gameFactory: null);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        await svc.JoinAsync("stub-bob", matchId, new JoinMatchRequest("stompy"), CancellationToken.None);
        return (svc, matchId);
    }

    // =======================================================================
    // ConcedeAsync tests
    // =======================================================================

    [Fact]
    public async Task ConcedeAsync_WhenNotPlaying_Returns409CannotConcede()
    {
        var (svc, matchId) = await NewServiceAndRollingMatchAsync();

        var result = await svc.ConcedeAsync("stub-alice", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("cannot-concede");
    }

    [Fact]
    public async Task ConcedeAsync_ByNonParty_ReturnsForbidden()
    {
        var (svc, _, matchId) = await NewServiceAndPlayingMatchAsync();

        var result = await svc.ConcedeAsync("stub-stranger", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("forbidden");
    }

    [Fact]
    public async Task ConcedeAsync_ByCreator_SetsWinnerToOpponentAndStateCompleted()
    {
        var (svc, pub, matchId) = await NewServiceAndPlayingMatchAsync();

        var result = await svc.ConcedeAsync("stub-alice", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.State.Should().Be("Completed");
        dto.WinnerSub.Should().Be("stub-bob");

        pub.Published.Should().Contain(e => e.@event == "match.state-changed");
    }

    [Fact]
    public async Task ConcedeAsync_ByOpponent_SetsWinnerToCreatorAndStateCompleted()
    {
        var (svc, _, matchId) = await NewServiceAndPlayingMatchAsync();

        var result = await svc.ConcedeAsync("stub-bob", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.State.Should().Be("Completed");
        dto.WinnerSub.Should().Be("stub-alice");
    }

    // =======================================================================
    // AbandonAsync tests
    // =======================================================================

    [Fact]
    public async Task AbandonAsync_ByNonCreator_ReturnsForbidden()
    {
        // Match is Open; bob (non-creator) tries to abandon
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource()), new StubDeckLoader(), new SystemClock(),
            null, timeoutScheduler: null, gameFactory: null);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        var result = await svc.AbandonAsync("stub-bob", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("forbidden");
    }

    [Fact]
    public async Task AbandonAsync_WhenStatePlaying_ReturnsMatchInProgress()
    {
        var (svc, _, matchId) = await NewServiceAndPlayingMatchAsync();

        var result = await svc.AbandonAsync("stub-alice", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("match-in-progress");
    }

    [Fact]
    public async Task AbandonAsync_WhenStateOpen_SetsStateAbandoned()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        var pub = new CapturePublisher();
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource()), new StubDeckLoader(), new SystemClock(),
            pub, timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask), gameFactory: null);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        var result = await svc.AbandonAsync("stub-alice", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        var match = await matchRepo.GetByIdAsync(matchId, CancellationToken.None);
        match!.State.Should().Be(MatchState.Abandoned);

        pub.Published.Should().Contain(e => e.@event == "match.state-changed");
    }

    [Fact]
    public async Task AbandonAsync_WhenStateRolling_SetsStateAbandoned()
    {
        var (svc, matchId) = await NewServiceAndRollingMatchAsync();

        var result = await svc.AbandonAsync("stub-alice", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task AbandonAsync_WhenStateJoined_SetsStateAbandoned()
    {
        // Directly insert a match in Joined state
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-bob", Handle = "bob", HandleDisplay = "Bob", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        var match = new Match
        {
            Id = Guid.NewGuid(),
            State = MatchState.Joined,
            Visibility = MatchVisibility.Public,
            Format = "constructed",
            ClockMinutes = 20,
            Creator = new MatchPlayer { Sub = "stub-alice", Handle = "Alice", DeckId = "burn", DeckSnapshot = new List<string>() },
            Opponent = new MatchPlayer { Sub = "stub-bob", Handle = "Bob", DeckId = "stompy", DeckSnapshot = new List<string>() },
            CreatorMillisRemaining = 1_200_000L,
            OpponentMillisRemaining = 1_200_000L,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await matchRepo.InsertAsync(match, CancellationToken.None);

        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource()), new StubDeckLoader(), new SystemClock(),
            null, timeoutScheduler: null, gameFactory: null);

        var result = await svc.AbandonAsync("stub-alice", match.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var updated = await matchRepo.GetByIdAsync(match.Id, CancellationToken.None);
        updated!.State.Should().Be(MatchState.Abandoned);
    }
}
