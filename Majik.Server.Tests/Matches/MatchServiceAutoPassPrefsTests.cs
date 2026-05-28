using FluentAssertions;
using Majik.Core.Api.Dtos;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// MatchService-level tests for Slice 5a's auto-pass prefs surface.
/// Validates the SetAutoPassPrefsAsync authz / validation gates and
/// the eviction wiring on Concede / Abandon / Timeout terminal paths.
/// </summary>
public class MatchServiceAutoPassPrefsTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceAutoPassPrefsTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class StubRandomSource : IRandomSource
    {
        private readonly Queue<int> _values;
        public StubRandomSource(params int[] values) => _values = new Queue<int>(values);
        public int NextInt(int min, int max) => _values.Dequeue();
    }

    /// <summary>
    /// Build a service + match-in-Playing state with an attached prefs
    /// store the caller can also reach. Mirrors the helper in
    /// MatchServiceConcedeAbandonTests but injects the prefs store.
    /// </summary>
    private async Task<(MatchService svc, AutoPassPrefsStore store, Guid matchId)>
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

        var store = new AutoPassPrefsStore();
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource(6, 2)), new StubDeckLoader(), new SystemClock(),
            null, timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask), gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy(),
            autoPassPrefs: store);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        await svc.JoinAsync("stub-bob", matchId, new JoinMatchRequest("stompy"), CancellationToken.None);
        await svc.SubmitRollAsync("stub-alice", matchId, CancellationToken.None);
        await svc.SubmitRollAsync("stub-bob", matchId, CancellationToken.None);
        await svc.PlayDrawAsync("stub-alice", matchId, new PlayDrawRequest("play"), CancellationToken.None);

        return (svc, store, matchId);
    }

    // =======================================================================
    // SetAutoPassPrefsAsync
    // =======================================================================

    [Fact]
    public async Task SetAutoPassPrefs_ByParty_PersistsAndReadsBack()
    {
        var (svc, store, matchId) = await NewServiceAndPlayingMatchAsync();
        var prefs = new AutoPassPrefs(
            FullControl: true,
            PhaseStops: new Dictionary<string, string> { ["Upkeep"] = "mine", ["End"] = "theirs" });

        var result = await svc.SetAutoPassPrefsAsync("stub-alice", matchId, prefs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var stored = store.Get(matchId, "stub-alice");
        stored.Should().BeSameAs(prefs);
    }

    [Fact]
    public async Task SetAutoPassPrefs_ByNonParty_Returns403()
    {
        var (svc, store, matchId) = await NewServiceAndPlayingMatchAsync();
        var prefs = new AutoPassPrefs(true, new Dictionary<string, string>());

        var result = await svc.SetAutoPassPrefsAsync("stub-stranger", matchId, prefs, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("forbidden");
        store.Has(matchId, "stub-stranger").Should().BeFalse();
        store.Has(matchId, "stub-alice").Should().BeFalse();
    }

    [Fact]
    public async Task SetAutoPassPrefs_NoMatch_Returns404()
    {
        var (svc, _, _) = await NewServiceAndPlayingMatchAsync();
        var prefs = new AutoPassPrefs(true, new Dictionary<string, string>());

        var result = await svc.SetAutoPassPrefsAsync(
            "stub-alice", Guid.NewGuid(), prefs, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("match-not-found");
    }

    [Fact]
    public async Task SetAutoPassPrefs_BadBody_Returns400()
    {
        var (svc, _, matchId) = await NewServiceAndPlayingMatchAsync();

        // null body
        var r1 = await svc.SetAutoPassPrefsAsync("stub-alice", matchId, null!, CancellationToken.None);
        r1.IsSuccess.Should().BeFalse();
        r1.Error!.Error.Should().Be("invalid-request");

        // PhaseStops missing
        var r2 = await svc.SetAutoPassPrefsAsync(
            "stub-alice", matchId, new AutoPassPrefs(false, null!), CancellationToken.None);
        r2.IsSuccess.Should().BeFalse();
        r2.Error!.Error.Should().Be("invalid-request");
    }

    [Fact]
    public async Task SetAutoPassPrefs_ByBothSeats_KeepsSeparateEntries()
    {
        var (svc, store, matchId) = await NewServiceAndPlayingMatchAsync();
        var alicePrefs = new AutoPassPrefs(true, new Dictionary<string, string>());
        var bobPrefs = new AutoPassPrefs(false, new Dictionary<string, string> { ["End"] = "mine" });

        await svc.SetAutoPassPrefsAsync("stub-alice", matchId, alicePrefs, CancellationToken.None);
        await svc.SetAutoPassPrefsAsync("stub-bob", matchId, bobPrefs, CancellationToken.None);

        store.Get(matchId, "stub-alice").FullControl.Should().BeTrue();
        store.Get(matchId, "stub-bob").PhaseStops["End"].Should().Be("mine");
    }

    [Fact]
    public async Task SetAutoPassPrefs_Overwrite_LastValueWins()
    {
        var (svc, store, matchId) = await NewServiceAndPlayingMatchAsync();
        await svc.SetAutoPassPrefsAsync(
            "stub-alice", matchId,
            new AutoPassPrefs(false, new Dictionary<string, string>()),
            CancellationToken.None);
        await svc.SetAutoPassPrefsAsync(
            "stub-alice", matchId,
            new AutoPassPrefs(true, new Dictionary<string, string> { ["End"] = "mine" }),
            CancellationToken.None);

        var got = store.Get(matchId, "stub-alice");
        got.FullControl.Should().BeTrue();
        got.PhaseStops["End"].Should().Be("mine");
    }

    // =======================================================================
    // Eviction on terminal-state transitions
    // =======================================================================

    [Fact]
    public async Task Concede_EvictsAutoPassPrefsForMatch()
    {
        var (svc, store, matchId) = await NewServiceAndPlayingMatchAsync();
        await svc.SetAutoPassPrefsAsync(
            "stub-alice", matchId,
            new AutoPassPrefs(true, new Dictionary<string, string>()),
            CancellationToken.None);
        await svc.SetAutoPassPrefsAsync(
            "stub-bob", matchId,
            new AutoPassPrefs(true, new Dictionary<string, string>()),
            CancellationToken.None);
        store.Count.Should().Be(2);

        var conceded = await svc.ConcedeAsync("stub-alice", matchId, CancellationToken.None);
        conceded.IsSuccess.Should().BeTrue();

        store.Has(matchId, "stub-alice").Should().BeFalse();
        store.Has(matchId, "stub-bob").Should().BeFalse();
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task Timeout_EvictsAutoPassPrefsForMatch()
    {
        var (svc, store, matchId) = await NewServiceAndPlayingMatchAsync();
        await svc.SetAutoPassPrefsAsync(
            "stub-alice", matchId,
            new AutoPassPrefs(true, new Dictionary<string, string>()),
            CancellationToken.None);
        await svc.SetAutoPassPrefsAsync(
            "stub-bob", matchId,
            new AutoPassPrefs(false, new Dictionary<string, string> { ["End"] = "mine" }),
            CancellationToken.None);

        await svc.OnTimeoutAsync(matchId, "stub-alice", CancellationToken.None);

        store.Has(matchId, "stub-alice").Should().BeFalse();
        store.Has(matchId, "stub-bob").Should().BeFalse();
    }

    [Fact]
    public async Task Abandon_EvictsAutoPassPrefsForMatch()
    {
        // Abandon requires the match to be non-Playing — build a match
        // that's only at Open and write prefs against its id directly.
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);
        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        var store = new AutoPassPrefsStore();
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource(6, 2)), new StubDeckLoader(), new SystemClock(),
            null, timeoutScheduler: null, gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy(),
            autoPassPrefs: store);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        await svc.SetAutoPassPrefsAsync(
            "stub-alice", matchId,
            new AutoPassPrefs(true, new Dictionary<string, string>()),
            CancellationToken.None);
        store.Has(matchId, "stub-alice").Should().BeTrue();

        var abandoned = await svc.AbandonAsync("stub-alice", matchId, CancellationToken.None);
        abandoned.IsSuccess.Should().BeTrue();

        store.Has(matchId, "stub-alice").Should().BeFalse();
    }

    [Fact]
    public async Task Concede_OtherMatchPrefs_StayPut()
    {
        var (svc, store, matchId) = await NewServiceAndPlayingMatchAsync();
        var otherMatch = Guid.NewGuid();
        await svc.SetAutoPassPrefsAsync(
            "stub-alice", matchId,
            new AutoPassPrefs(true, new Dictionary<string, string>()),
            CancellationToken.None);
        // Synthesize a stray entry for a different match (the bypass path
        // — SetAutoPassPrefsAsync would 404 on a non-existent match).
        store.Set(otherMatch, "stub-alice", new AutoPassPrefs(true, new Dictionary<string, string>()));

        await svc.ConcedeAsync("stub-alice", matchId, CancellationToken.None);

        store.Has(matchId, "stub-alice").Should().BeFalse();
        store.Has(otherMatch, "stub-alice").Should().BeTrue();
    }
}
