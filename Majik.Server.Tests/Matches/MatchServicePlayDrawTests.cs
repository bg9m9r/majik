using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchServicePlayDrawTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServicePlayDrawTests(TestMongoFixture fixture) => _fixture = fixture;

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
    /// Creates a service + match in Rolling state.
    /// StubRandomSource values are the two dice rolls (creator, opponent).
    /// Returns the matchId plus the winner sub based on the rolls.
    /// </summary>
    private async Task<(MatchService svc, CapturePublisher pub, Guid matchId, string winnerSub)>
        NewServiceAndRollingMatchAsync(StubRandomSource rng)
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
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(rng), new StubDeckLoader(), new SystemClock(),
            pub, timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask), gameFactory: null);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        var joined = await svc.JoinAsync("stub-bob", matchId,
            new JoinMatchRequest("stompy"), CancellationToken.None);
        var winnerSub = joined.Value!.Roll!.WinnerSub;

        pub.Published.Clear();
        return (svc, pub, matchId, winnerSub);
    }

    // -----------------------------------------------------------------------
    // Non-winner caller → 403 not-roll-winner
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDrawAsync_ByNonWinner_Returns403NotRollWinner()
    {
        // alice wins (roll 6 > 2)
        var (svc, _, matchId, winnerSub) = await NewServiceAndRollingMatchAsync(new StubRandomSource(6, 2));
        winnerSub.Should().Be("stub-alice");
        var loserSub = "stub-bob";

        var result = await svc.PlayDrawAsync(loserSub, matchId,
            new PlayDrawRequest("play"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("not-roll-winner");
    }

    // -----------------------------------------------------------------------
    // Wrong state → 409 not-rolling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDrawAsync_WhenNotRolling_Returns409NotRolling()
    {
        // Use a separate match still in Open state (before join)
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

        // Match is Open, not Rolling
        var result = await svc.PlayDrawAsync("stub-alice", matchId,
            new PlayDrawRequest("play"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("not-rolling");
    }

    // -----------------------------------------------------------------------
    // Invalid choice → 400 invalid-choice
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDrawAsync_InvalidChoice_Returns400InvalidChoice()
    {
        var (svc, _, matchId, winnerSub) = await NewServiceAndRollingMatchAsync(new StubRandomSource(6, 2));

        var result = await svc.PlayDrawAsync(winnerSub, matchId,
            new PlayDrawRequest("mulligan"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("invalid-choice");
    }

    // -----------------------------------------------------------------------
    // choice=play with creator-winner → state=Playing, priorityHolder=creator (winner)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDrawAsync_ChoicePlay_CreatorWins_PriorityHolderIsCreator()
    {
        // alice (creator) wins with roll 6 vs 2
        var (svc, pub, matchId, winnerSub) = await NewServiceAndRollingMatchAsync(new StubRandomSource(6, 2));
        winnerSub.Should().Be("stub-alice");

        var result = await svc.PlayDrawAsync(winnerSub, matchId,
            new PlayDrawRequest("play"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.State.Should().Be("Playing");
        dto.FirstChoice.Should().Be("play");
        dto.PriorityHolderSub.Should().Be("stub-alice"); // winner goes first when choice=play
        dto.PriorityStartedAt.Should().NotBeNull();

        pub.Published.Select(e => e.@event).Should().Contain("match.play-draw-chosen");
        pub.Published.Select(e => e.@event).Should().Contain("match.state-changed");
    }

    // -----------------------------------------------------------------------
    // choice=draw with creator-winner → state=Playing, priorityHolder=opponent (loser)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDrawAsync_ChoiceDraw_CreatorWins_PriorityHolderIsOpponent()
    {
        // alice (creator) wins with roll 6 vs 2
        var (svc, pub, matchId, winnerSub) = await NewServiceAndRollingMatchAsync(new StubRandomSource(6, 2));
        winnerSub.Should().Be("stub-alice");

        var result = await svc.PlayDrawAsync(winnerSub, matchId,
            new PlayDrawRequest("draw"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.State.Should().Be("Playing");
        dto.FirstChoice.Should().Be("draw");
        dto.PriorityHolderSub.Should().Be("stub-bob"); // loser goes first when choice=draw
        dto.PriorityStartedAt.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // choice=play with opponent-winner → state=Playing, priorityHolder=opponent (winner)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDrawAsync_ChoicePlay_OpponentWins_PriorityHolderIsOpponent()
    {
        // bob (opponent) wins with roll 1 vs 6
        var (svc, _, matchId, winnerSub) = await NewServiceAndRollingMatchAsync(new StubRandomSource(1, 6));
        winnerSub.Should().Be("stub-bob");

        var result = await svc.PlayDrawAsync(winnerSub, matchId,
            new PlayDrawRequest("play"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.State.Should().Be("Playing");
        dto.PriorityHolderSub.Should().Be("stub-bob"); // winner (opponent) goes first
    }

    // -----------------------------------------------------------------------
    // choice=draw with opponent-winner → state=Playing, priorityHolder=creator (loser)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlayDrawAsync_ChoiceDraw_OpponentWins_PriorityHolderIsCreator()
    {
        // bob (opponent) wins with roll 1 vs 6
        var (svc, _, matchId, winnerSub) = await NewServiceAndRollingMatchAsync(new StubRandomSource(1, 6));
        winnerSub.Should().Be("stub-bob");

        var result = await svc.PlayDrawAsync(winnerSub, matchId,
            new PlayDrawRequest("draw"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.State.Should().Be("Playing");
        dto.PriorityHolderSub.Should().Be("stub-alice"); // loser (creator) goes first when choice=draw
    }
}
