using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Tests for OnPriorityPassedAsync and OnTimeoutAsync.
/// A FakeClock controls UtcNow so elapsed time is fully deterministic.
/// </summary>
public class MatchServiceClockTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceClockTests(TestMongoFixture fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // Fakes / stubs
    // -----------------------------------------------------------------------

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

    private sealed class CaptureScheduler
    {
        public List<(Guid matchId, string holderSub, long remainingMs)> Scheduled { get; } = new();
        public List<Guid> Cancelled { get; } = new();

        public MatchTimeoutScheduler AsScheduler() =>
            new MatchTimeoutScheduler((id, sub, ct) => Task.CompletedTask)
            {
            };

        // Returns a MatchTimeoutScheduler whose Schedule/Cancel we can observe
        // by wrapping callbacks. We'll use a custom approach below.
    }

    /// <summary>
    /// A thin wrapper that captures Schedule/Cancel calls without using real timers.
    /// </summary>
    private sealed class SpyScheduler
    {
        public record ScheduleCall(Guid MatchId, string HolderSub, long RemainingMs);
        public List<ScheduleCall> ScheduleCalls { get; } = new();
        public List<Guid> CancelCalls { get; } = new();

        public MatchTimeoutScheduler ToMatchTimeoutScheduler()
        {
            // We pass a no-op callback and set very large delays so it never fires.
            // The spy-capture happens by subclassing via a lambda that records args.
            // Since MatchTimeoutScheduler is sealed, we capture via a custom subclass-like
            // approach: use a real scheduler but with a very long delay; we only test
            // that Schedule was called with correct args by checking our lists.
            // To intercept Schedule/Cancel we use a small shim approach below.
            throw new NotSupportedException("Use SpyScheduler.Capture* methods directly on MatchService via reflection or redesign.");
        }
    }

    // -----------------------------------------------------------------------
    // Helper: build a MatchService with a FakeClock and in-memory Mongo repo,
    // then insert a match already in Playing state.
    // -----------------------------------------------------------------------

    private async Task<(MatchService svc, MatchRepository repo, FakeClock clock, CapturePublisher pub, Guid matchId)>
        SetupPlayingMatchAsync(
            string creatorSub,
            string opponentSub,
            string initialHolderSub,
            long creatorMs,
            long opponentMs,
            DateTime? priorityStartedAt = null)
    {
        var db = _fixture.NewDatabase();
        var repo = new MatchRepository(db);
        await repo.EnsureIndexesAsync(CancellationToken.None);

        var clock = new FakeClock();
        var pub = new CapturePublisher();

        // Use a no-op scheduler (long delay) so we don't interfere with timers.
        var scheduler = new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask);

        var svc = new MatchService(
            repo,
            profiles: null!,    // not needed for clock tests
            dice: null!,
            decks: null!,
            clock,
            pub,
            scheduler,
            gameFactory: null);

        var startedAt = priorityStartedAt ?? clock.UtcNow;
        var match = new Match
        {
            Id = Guid.NewGuid(),
            State = MatchState.Playing,
            Visibility = MatchVisibility.Public,
            Format = "constructed",
            ClockMinutes = 20,
            Creator = new MatchPlayer { Sub = creatorSub, Handle = "Alice", DeckId = "burn" },
            Opponent = new MatchPlayer { Sub = opponentSub, Handle = "Bob", DeckId = "stompy" },
            CreatorMillisRemaining = creatorMs,
            OpponentMillisRemaining = opponentMs,
            PriorityHolderSub = initialHolderSub,
            PriorityStartedAt = startedAt,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        await repo.InsertAsync(match, CancellationToken.None);
        return (svc, repo, clock, pub, match.Id);
    }

    // -----------------------------------------------------------------------
    // Test 1: Priority transfer — alice → bob, alice's remaining decrements
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnPriorityPassed_TransfersHolder_AndDecreasesFromPrevHolder()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";
        const long initialCreatorMs = 1_200_000L; // 20 min
        const long initialOpponentMs = 1_200_000L;

        var (svc, repo, clock, pub, matchId) = await SetupPlayingMatchAsync(
            creatorSub: alice,
            opponentSub: bob,
            initialHolderSub: alice,
            creatorMs: initialCreatorMs,
            opponentMs: initialOpponentMs);

        // Advance clock by 5 seconds (5000 ms). Alice held priority during those 5s.
        clock.UtcNow = clock.UtcNow.AddMilliseconds(5_000);

        await svc.OnPriorityPassedAsync(matchId, newHolderSub: bob, CancellationToken.None);

        var fresh = await repo.GetByIdAsync(matchId, CancellationToken.None);
        fresh.Should().NotBeNull();
        fresh!.State.Should().Be(MatchState.Playing);
        fresh.PriorityHolderSub.Should().Be(bob);
        fresh.CreatorMillisRemaining.Should().Be(initialCreatorMs - 5_000); // alice lost 5000ms
        fresh.OpponentMillisRemaining.Should().Be(initialOpponentMs);       // bob unchanged

        pub.Published.Select(e => e.@event).Should().Contain("match.clock-update");
    }

    // -----------------------------------------------------------------------
    // Test 2: Multiple transfers accumulate correctly across both players
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnPriorityPassed_MultipleTransfers_AccumulateCorrectly()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";
        const long start = 1_200_000L;

        var (svc, repo, clock, _, matchId) = await SetupPlayingMatchAsync(
            creatorSub: alice,
            opponentSub: bob,
            initialHolderSub: alice,
            creatorMs: start,
            opponentMs: start);

        var t0 = clock.UtcNow;

        // Transfer 1: alice holds 3s, then passes to bob
        clock.UtcNow = t0.AddMilliseconds(3_000);
        await svc.OnPriorityPassedAsync(matchId, newHolderSub: bob, CancellationToken.None);

        // Transfer 2: bob holds 7s, then passes to alice
        clock.UtcNow = t0.AddMilliseconds(10_000); // 3s for alice + 7s for bob
        await svc.OnPriorityPassedAsync(matchId, newHolderSub: alice, CancellationToken.None);

        // Transfer 3: alice holds 2s, then passes to bob
        clock.UtcNow = t0.AddMilliseconds(12_000);
        await svc.OnPriorityPassedAsync(matchId, newHolderSub: bob, CancellationToken.None);

        var fresh = await repo.GetByIdAsync(matchId, CancellationToken.None);
        fresh!.CreatorMillisRemaining.Should().Be(start - 3_000 - 2_000); // alice spent 3+2 = 5s
        fresh.OpponentMillisRemaining.Should().Be(start - 7_000);          // bob spent 7s
        fresh.PriorityHolderSub.Should().Be(bob);
    }

    // -----------------------------------------------------------------------
    // Test 3: Remaining <= 0 on transfer → triggers timeout inline (state=Completed)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnPriorityPassed_WhenRemainingExhausted_TriggersTimeout()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";
        const long tinyMs = 1_000L; // alice only has 1 second left

        var (svc, repo, clock, pub, matchId) = await SetupPlayingMatchAsync(
            creatorSub: alice,
            opponentSub: bob,
            initialHolderSub: alice,
            creatorMs: tinyMs,
            opponentMs: 1_200_000L);

        // Advance 5 seconds — alice's 1s balance is fully consumed
        clock.UtcNow = clock.UtcNow.AddMilliseconds(5_000);

        await svc.OnPriorityPassedAsync(matchId, newHolderSub: bob, CancellationToken.None);

        var fresh = await repo.GetByIdAsync(matchId, CancellationToken.None);
        fresh!.State.Should().Be(MatchState.Completed);
        fresh.TimeoutLoserSub.Should().Be(alice);
        fresh.WinnerSub.Should().Be(bob);

        pub.Published.Select(e => e.@event).Should().Contain("match.timed-out");
        pub.Published.Select(e => e.@event).Should().Contain("match.state-changed");
    }

    // -----------------------------------------------------------------------
    // Test 4: OnTimeoutAsync called directly sets state=Completed, winner=other
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnTimeoutAsync_SetsCompletedAndWinnerIsOtherPlayer()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";

        var (svc, repo, clock, pub, matchId) = await SetupPlayingMatchAsync(
            creatorSub: alice,
            opponentSub: bob,
            initialHolderSub: bob,
            creatorMs: 1_200_000L,
            opponentMs: 1_200_000L);

        // Bob times out — alice should win
        await svc.OnTimeoutAsync(matchId, loserSub: bob, CancellationToken.None);

        var fresh = await repo.GetByIdAsync(matchId, CancellationToken.None);
        fresh!.State.Should().Be(MatchState.Completed);
        fresh.WinnerSub.Should().Be(alice);
        fresh.TimeoutLoserSub.Should().Be(bob);

        pub.Published.Select(e => e.@event).Should().Contain("match.timed-out");
        pub.Published.Select(e => e.@event).Should().Contain("match.state-changed");
        pub.Published.Where(e => e.@event == "match.state-changed").Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    // Test 5: OnTimeoutAsync is idempotent — second call is a no-op
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OnTimeoutAsync_WhenAlreadyCompleted_IsNoOp()
    {
        const string alice = "stub-alice";
        const string bob = "stub-bob";

        var (svc, repo, clock, pub, matchId) = await SetupPlayingMatchAsync(
            creatorSub: alice,
            opponentSub: bob,
            initialHolderSub: alice,
            creatorMs: 1_200_000L,
            opponentMs: 1_200_000L);

        await svc.OnTimeoutAsync(matchId, loserSub: alice, CancellationToken.None);
        pub.Published.Clear();

        // Second call should be ignored (state is now Completed, not Playing)
        await svc.OnTimeoutAsync(matchId, loserSub: alice, CancellationToken.None);

        pub.Published.Should().BeEmpty();
        var fresh = await repo.GetByIdAsync(matchId, CancellationToken.None);
        fresh!.WinnerSub.Should().Be(bob); // unchanged from first call
    }
}
