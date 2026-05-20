using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchServiceSubmitRollTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;

    public MatchServiceSubmitRollTests(TestMongoFixture fixture) => _fixture = fixture;

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

    private async Task<(MatchService svc, CapturePublisher pub, Guid matchId)>
        NewServiceAndRollingMatchAsync(StubRandomSource rng, string creatorSub = "u-alice", string opponentSub = "u-bob")
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = creatorSub, Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);
        await profileRepo.UpsertAsync(new UserProfile { Sub = opponentSub, Handle = "bob", HandleDisplay = "Bob", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        var pub = new CapturePublisher();
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(rng), new StubDeckLoader(), new SystemClock(),
            pub, timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask), gameFactory: null);

        var created = await svc.CreateAsync(creatorSub,
            new CreateMatchRequest("constructed", "public", "starter", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        await svc.JoinAsync(opponentSub, matchId,
            new JoinMatchRequest("starter"), CancellationToken.None);

        pub.Published.Clear();
        return (svc, pub, matchId);
    }

    // -----------------------------------------------------------------------
    // Creator submits roll — CreatorRoll slot filled, opponent still null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitRoll_SetsCallerSlot()
    {
        var (svc, _, matchId) = await NewServiceAndRollingMatchAsync(new StubRandomSource(4));

        var r = await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Roll.Should().NotBeNull();
        r.Value.Roll!.CreatorRoll.Should().Be(4);
        r.Value.Roll.OpponentRoll.Should().BeNull();
        r.Value.Roll.WinnerSub.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Calling twice for same player is idempotent — second call returns cached roll
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitRoll_Idempotent_ReturnsCurrentSnapshot()
    {
        var (svc, _, matchId) = await NewServiceAndRollingMatchAsync(new StubRandomSource(4, 99)); // 99 should never be consumed

        await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);
        var r2 = await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);

        r2.IsSuccess.Should().BeTrue();
        r2.Value!.Roll!.CreatorRoll.Should().Be(4); // still 4, not 99
    }

    // -----------------------------------------------------------------------
    // Both players roll — winner computed, state stays Rolling until PlayDraw
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitRoll_ComputesWinnerWhenBothFilled()
    {
        var (svc, _, matchId) = await NewServiceAndRollingMatchAsync(new StubRandomSource(6, 2)); // alice rolls 6, bob rolls 2

        await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);
        var r = await svc.SubmitRollAsync("u-bob", matchId, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Roll!.WinnerSub.Should().Be("u-alice");
        r.Value.Roll.CreatorRoll.Should().Be(6);
        r.Value.Roll.OpponentRoll.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Tie auto-rerolls until different
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitRoll_TieAutoRerolls()
    {
        // alice rolls 3, bob rolls 3 (tie), reroll cycle: alice 5, bob 1 → alice wins
        var (svc, _, matchId) = await NewServiceAndRollingMatchAsync(new StubRandomSource(3, 3, 5, 1));

        await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);
        var r = await svc.SubmitRollAsync("u-bob", matchId, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Roll!.WinnerSub.Should().Be("u-alice");
        r.Value.Roll.CreatorRoll.Should().Be(5);
        r.Value.Roll.OpponentRoll.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Non-player caller → not-a-player error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitRoll_NotPlayer_ReturnsForbidden()
    {
        var (svc, _, matchId) = await NewServiceAndRollingMatchAsync(new StubRandomSource(4));

        var r = await svc.SubmitRollAsync("u-mallory", matchId, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("not-a-player");
    }

    // -----------------------------------------------------------------------
    // Match in wrong state → not-rolling error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitRoll_NotRolling_ReturnsConflict()
    {
        // 5, 2 = alice wins; alice chooses play → state moves to Playing
        var (svc, _, matchId) = await NewServiceAndRollingMatchAsync(new StubRandomSource(5, 2));

        await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);
        await svc.SubmitRollAsync("u-bob", matchId, CancellationToken.None);
        await svc.PlayDrawAsync("u-alice", matchId, new PlayDrawRequest("play"), CancellationToken.None);

        var r = await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("not-rolling");
    }

    // -----------------------------------------------------------------------
    // Match not found → match-not-found error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitRoll_MatchNotFound_ReturnsNotFound()
    {
        var (svc, _, _) = await NewServiceAndRollingMatchAsync(new StubRandomSource(4));

        var r = await svc.SubmitRollAsync("u-alice", Guid.NewGuid(), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error!.Error.Should().Be("match-not-found");
    }
}
