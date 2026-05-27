using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Tests.Profiles;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Slice 4a reliability + concurrency coverage that mirrors
/// <see cref="MatchServiceClockTests"/> (FakeClock, embedded Mongo). Covers:
///   * retry-with-backoff succeeds after N transient Mongo faults (#2),
///   * clock clamps to ≥0 so skew can't persist a negative balance (#4),
///   * a duplicate clock handoff (same holder + startedAt) is rejected by the
///     tightened CAS (#6).
/// </summary>
public class MatchServiceReliabilityTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceReliabilityTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class CapturePublisher : IMatchHubPublisher
    {
        public List<(Guid matchId, string @event, object payload)> Published { get; } = new();
        public void Publish(Guid matchId, string @event, object payload) =>
            Published.Add((matchId, @event, payload));
    }

    /// <summary>
    /// Repo wrapper that throws a transient Mongo fault on the first
    /// <paramref name="faultsBeforeSuccess"/> calls to
    /// <see cref="TryAtomicUpdateAsync"/>, then delegates to the real Mongo
    /// implementation. Lets the retry policy exercise its backoff loop
    /// against a genuine embedded Mongo for the eventual success.
    /// </summary>
    private sealed class FaultInjectingRepo : MatchRepository
    {
        private readonly int _faultsBeforeSuccess;
        public int AtomicUpdateAttempts { get; private set; }

        public FaultInjectingRepo(IMongoDatabase database, int faultsBeforeSuccess)
            : base(database)
        {
            _faultsBeforeSuccess = faultsBeforeSuccess;
        }

        public override Task<bool> TryAtomicUpdateAsync(
            Guid id, MatchState expectedState, UpdateDefinition<Match> update, CancellationToken ct)
        {
            AtomicUpdateAttempts++;
            if (AtomicUpdateAttempts <= _faultsBeforeSuccess)
            {
                // A timeout is classified transient by RetryPolicy.IsTransient.
                throw new TimeoutException(
                    $"injected transient fault #{AtomicUpdateAttempts}");
            }
            return base.TryAtomicUpdateAsync(id, expectedState, update, ct);
        }
    }

    private static Match NewPlayingMatch(
        Guid id, string alice, string bob, string holder, DateTime startedAt,
        long creatorMs = 1_200_000L, long opponentMs = 1_200_000L) => new()
    {
        Id = id,
        State = MatchState.Playing,
        Visibility = MatchVisibility.Public,
        Format = "constructed",
        ClockMinutes = 20,
        Creator = new MatchPlayer { Sub = alice, Handle = "Alice", DeckId = "burn", DeckSnapshot = new List<string>() },
        Opponent = new MatchPlayer { Sub = bob, Handle = "Bob", DeckId = "stompy", DeckSnapshot = new List<string>() },
        CreatorMillisRemaining = creatorMs,
        OpponentMillisRemaining = opponentMs,
        PriorityHolderSub = holder,
        PriorityStartedAt = startedAt,
        CreatedAt = startedAt,
        UpdatedAt = startedAt,
    };

    private MatchService BuildService(MatchRepository repo, FakeClock clock, CapturePublisher pub) =>
        new MatchService(
            repo,
            profiles: null!,
            dice: null!,
            decks: null!,
            clock,
            pub,
            timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask),
            gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy());

    // -----------------------------------------------------------------------
    // #2 — retry succeeds after N transient Mongo faults; match reaches Completed.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnTimeout_RetriesThroughTransientFaults_ReachesCompleted()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";

        var db = _fixture.NewDatabase();
        // 3 transient faults, then the 4th attempt (DefaultMaxAttempts) succeeds.
        var repo = new FaultInjectingRepo(db, faultsBeforeSuccess: 3);
        await repo.EnsureIndexesAsync(CancellationToken.None);

        var clock = new FakeClock();
        var pub = new CapturePublisher();
        var svc = BuildService(repo, clock, pub);

        var matchId = Guid.NewGuid();
        await repo.InsertAsync(
            NewPlayingMatch(matchId, alice, bob, holder: bob, startedAt: clock.UtcNow),
            CancellationToken.None);

        // Bob times out — alice should win, despite the first 3 CAS attempts
        // failing with transient faults.
        await svc.OnTimeoutAsync(matchId, loserSub: bob, CancellationToken.None);

        repo.AtomicUpdateAttempts.Should().Be(RetryPolicy.DefaultMaxAttempts,
            "the CAS should be retried until the 4th attempt lands");

        var fresh = await repo.GetByIdAsync(matchId, CancellationToken.None);
        fresh!.State.Should().Be(MatchState.Completed, "the retry must drive the match to a terminal state");
        fresh.WinnerSub.Should().Be(alice);
        fresh.TimeoutLoserSub.Should().Be(bob);
        pub.Published.Select(e => e.@event).Should().Contain("match.timed-out");
    }

    [Fact]
    public async Task OnTimeout_ExhaustsRetries_ThrowsLoud_DoesNotSilentlyFreeze()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";

        var db = _fixture.NewDatabase();
        // More faults than the attempt budget → every attempt throws.
        var repo = new FaultInjectingRepo(db, faultsBeforeSuccess: 10);
        await repo.EnsureIndexesAsync(CancellationToken.None);

        var clock = new FakeClock();
        var pub = new CapturePublisher();
        var svc = BuildService(repo, clock, pub);

        var matchId = Guid.NewGuid();
        await repo.InsertAsync(
            NewPlayingMatch(matchId, alice, bob, holder: bob, startedAt: clock.UtcNow),
            CancellationToken.None);

        // Exhausting the budget must FAIL LOUD (rethrow) rather than return
        // quietly and leave the match frozen with no signal.
        Func<Task> act = () => svc.OnTimeoutAsync(matchId, loserSub: bob, CancellationToken.None);
        await act.Should().ThrowAsync<TimeoutException>();

        repo.AtomicUpdateAttempts.Should().Be(RetryPolicy.DefaultMaxAttempts);

        var fresh = await repo.GetByIdAsync(matchId, CancellationToken.None);
        fresh!.State.Should().Be(MatchState.Playing, "the doc is untouched, but the caller saw the failure");
    }

    // -----------------------------------------------------------------------
    // #4 — clock clamps to ≥0. A PriorityStartedAt slightly in the FUTURE
    // (clock skew) would make elapsed negative and credit the holder time.
    // The clamp keeps elapsed ≥0 so the stored balance is never inflated and
    // never negative.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnPriorityPassed_FutureStartedAt_DoesNotCreditHolder()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";
        const long start = 1_200_000L;

        var db = _fixture.NewDatabase();
        var repo = new MatchRepository(db);
        await repo.EnsureIndexesAsync(CancellationToken.None);

        var clock = new FakeClock();
        var pub = new CapturePublisher();
        var svc = BuildService(repo, clock, pub);

        var matchId = Guid.NewGuid();
        // PriorityStartedAt is 5s in the FUTURE relative to clock.UtcNow.
        await repo.InsertAsync(
            NewPlayingMatch(matchId, alice, bob, holder: alice,
                startedAt: clock.UtcNow.AddMilliseconds(5_000),
                creatorMs: start, opponentMs: start),
            CancellationToken.None);

        await svc.OnPriorityPassedAsync(matchId, newHolderSub: bob, CancellationToken.None);

        var fresh = await repo.GetByIdAsync(matchId, CancellationToken.None);
        fresh!.PriorityHolderSub.Should().Be(bob);
        // elapsed clamps to 0 → alice billed 0, NOT credited +5000.
        fresh.CreatorMillisRemaining.Should().Be(start,
            "negative elapsed (future startedAt) must clamp to 0, never credit the holder");
        fresh.OpponentMillisRemaining.Should().Be(start);
    }

    // -----------------------------------------------------------------------
    // #6 — duplicate / late clock handoff rejected by the tightened CAS.
    // A second handoff that observed the SAME (holder, startedAt) as the
    // winner must no-op once the winner has advanced PriorityStartedAt, even
    // though the holder it names is unchanged.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAtomicUpdateWithHolder_StaleStartedAt_RejectedByCas()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";
        const long start = 1_200_000L;

        var db = _fixture.NewDatabase();
        var repo = new MatchRepository(db);
        await repo.EnsureIndexesAsync(CancellationToken.None);

        var matchId = Guid.NewGuid();
        var startedAt = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        await repo.InsertAsync(
            NewPlayingMatch(matchId, alice, bob, holder: alice, startedAt: startedAt,
                creatorMs: start, opponentMs: start),
            CancellationToken.None);

        // Winner: deduct alice 5s, KEEP holder=alice (a same-active-player
        // re-fire) but advance PriorityStartedAt.
        var advanced = startedAt.AddMilliseconds(5_000);
        var winnerUpdate = Builders<Match>.Update
            .Set(m => m.CreatorMillisRemaining, start - 5_000)
            .Set(m => m.PriorityStartedAt, advanced);
        var winnerMoved = await repo.TryAtomicUpdateWithHolderAsync(
            matchId, MatchState.Playing, alice, winnerUpdate, CancellationToken.None,
            constrainStartedAt: true, expectedPriorityStartedAt: startedAt);
        winnerMoved.Should().BeTrue("setup: the winner CAS must match holder + original startedAt");

        // Duplicate handoff: same holder (alice), same STALE startedAt. The
        // winner already advanced the timestamp, so this must miss → no
        // second deduction off the same slice.
        var dupUpdate = Builders<Match>.Update
            .Set(m => m.CreatorMillisRemaining, start - 10_000)
            .Set(m => m.PriorityStartedAt, startedAt.AddMilliseconds(9_000));
        var dupMoved = await repo.TryAtomicUpdateWithHolderAsync(
            matchId, MatchState.Playing, alice, dupUpdate, CancellationToken.None,
            constrainStartedAt: true, expectedPriorityStartedAt: startedAt /* stale */);

        dupMoved.Should().BeFalse("a duplicate handoff carrying the stale startedAt must be rejected");

        var stored = await repo.GetByIdAsync(matchId, CancellationToken.None);
        stored!.CreatorMillisRemaining.Should().Be(start - 5_000, "alice billed exactly once");
        stored.PriorityStartedAt.Should().Be(advanced);
    }
}
