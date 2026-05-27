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

    /// <summary>Thread-safe sequence source for the concurrent-submission
    /// test. Hands out values from a queue under a lock so two parallel
    /// SubmitRollAsync calls can dequeue safely; falls back to a fixed value
    /// once exhausted so tie-rerolls (if any) never throw.</summary>
    private sealed class ConcurrentStubRandomSource : IRandomSource
    {
        private readonly Queue<int> _values;
        private readonly int _fallback;
        private readonly object _gate = new();
        public ConcurrentStubRandomSource(int fallback, params int[] values)
        {
            _values = new Queue<int>(values);
            _fallback = fallback;
        }
        public int NextInt(int min, int max)
        {
            lock (_gate) return _values.Count > 0 ? _values.Dequeue() : _fallback;
        }
    }

    private Task<(MatchService svc, CapturePublisher pub, Guid matchId)>
        NewServiceAndRollingMatchAsync(StubRandomSource rng, string creatorSub = "u-alice", string opponentSub = "u-bob")
        => NewServiceAndRollingMatchAsync((IRandomSource)rng, creatorSub, opponentSub);

    private async Task<(MatchService svc, CapturePublisher pub, Guid matchId)>
        NewServiceAndRollingMatchAsync(IRandomSource rng, string creatorSub = "u-alice", string opponentSub = "u-bob")
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
            pub, timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask), gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy());

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

    // -----------------------------------------------------------------------
    // #5 — two CONCURRENT submissions (one per seat) must not lose-update.
    // The old code read the same match.Roll, mutated in-process, and wrote the
    // whole object back gated only on State==Rolling, so a roll value or the
    // winner could be lost. The field-targeted CAS keeps both rolls + computes
    // exactly one winner. Repeated to shake out the race window.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SubmitRoll_ConcurrentSubmissions_BothRollsSurvive_SingleWinner()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            // 6 and 2 are distinct (no tie); fallback 4 keeps any stray
            // tie-reroll deterministic-ish without throwing. The order in
            // which the two concurrent calls dequeue is nondeterministic —
            // the invariant under test is that BOTH rolls land and a single
            // correct winner is chosen, regardless of who got which value.
            var rng = new ConcurrentStubRandomSource(fallback: 4, 6, 2);
            var (svc, _, matchId) = await NewServiceAndRollingMatchAsync(rng);

            var t1 = Task.Run(() => svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None));
            var t2 = Task.Run(() => svc.SubmitRollAsync("u-bob", matchId, CancellationToken.None));
            await Task.WhenAll(t1, t2);

            // Re-read authoritative state.
            var fresh = await svc.GetAsync("u-alice", matchId, CancellationToken.None);
            fresh.IsSuccess.Should().BeTrue();
            var roll = fresh.Value!.Roll;
            roll.Should().NotBeNull($"iteration {iteration}: roll record must exist");
            roll!.CreatorRoll.Should().NotBeNull($"iteration {iteration}: alice's roll must not be lost");
            roll.OpponentRoll.Should().NotBeNull($"iteration {iteration}: bob's roll must not be lost");
            roll.WinnerSub.Should().NotBeNull($"iteration {iteration}: a winner must be computed once both rolled");

            // Winner is the seat with the strictly-higher value (no tie since
            // values differ); the stored values must be internally consistent
            // with the winner.
            roll.CreatorRoll!.Value.Should().NotBe(roll.OpponentRoll!.Value,
                $"iteration {iteration}: tie must have been rerolled away");
            var expectedWinner = roll.CreatorRoll.Value > roll.OpponentRoll.Value ? "u-alice" : "u-bob";
            roll.WinnerSub.Should().Be(expectedWinner, $"iteration {iteration}: winner must match the higher roll");
        }
    }
}
