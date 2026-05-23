using System.Text.Json;
using FluentAssertions;
using Majik.Bot.Diagnostics;
using Majik.Core.Api.Dtos;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Tests for the GetReplayAsync flow on <see cref="MatchService"/>. The
/// replay buffer is wired live in production via the
/// <see cref="MatchFacadeBridge"/>; here we test the auth + lookup path
/// independently by pre-populating the buffer for a created match.
/// </summary>
public sealed class MatchServiceReplayTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceReplayTests(TestMongoFixture fixture) => _fixture = fixture;

    private sealed class StubRandomSource : IRandomSource
    {
        private readonly Queue<int> _values;
        public StubRandomSource(params int[] values) => _values = new Queue<int>(values);
        public int NextInt(int min, int max) => _values.Dequeue();
    }

    private static EventDto FakeEvent(string type) => new(
        EventId: Guid.NewGuid(),
        Type: type,
        At: DateTime.UtcNow,
        Payload: JsonDocument.Parse("""{"x":1}""").RootElement.Clone());

    /// <summary>Build a service backed by a real (test) Mongo + replay
    /// buffer, then create + join a match so we have a real document
    /// referencing two real subs. Returns the match id and the buffer
    /// so the test can prepopulate replay entries before the assertion.</summary>
    private async Task<(MatchService svc, MatchReplayBuffer buf, Guid matchId)> NewMatchAsync()
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-alice", Handle = "alice", HandleDisplay = "Alice", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);
        await profileRepo.UpsertAsync(new UserProfile { Sub = "stub-bob", Handle = "bob", HandleDisplay = "Bob", CreatedAt = now, UpdatedAt = now }, CancellationToken.None);

        var buf = new MatchReplayBuffer(new SystemClock(), NullLogger<MatchReplayBuffer>.Instance);
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource(6, 2)), new StubDeckLoader(), new SystemClock(),
            hub: null,
            timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask),
            gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy(),
            replayBuffer: buf);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;
        await svc.JoinAsync("stub-bob", matchId, new JoinMatchRequest("stompy"), CancellationToken.None);
        return (svc, buf, matchId);
    }

    [Fact]
    public async Task GetReplay_NonExistentMatch_NotFound()
    {
        var (svc, _, _) = await NewMatchAsync();
        var result = await svc.GetReplayAsync("stub-alice", Guid.NewGuid(), CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("match-not-found");
    }

    [Fact]
    public async Task GetReplay_NonParty_Forbidden()
    {
        var (svc, buf, matchId) = await NewMatchAsync();
        // Populate so the lookup wouldn't be cut short by an empty buffer.
        buf.RecordEvent(matchId, FakeEvent("TurnStartedEvent"));

        var result = await svc.GetReplayAsync("stub-stranger", matchId, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("forbidden");
    }

    [Fact]
    public async Task GetReplay_PartyMember_ReturnsCapturedEntries_InOrder()
    {
        var (svc, buf, matchId) = await NewMatchAsync();
        buf.RecordEvent(matchId, FakeEvent("TurnStartedEvent"));
        buf.RecordDecision(matchId, new BotDecision(
            "Priority", "Pass", 0.1,
            Array.Empty<BotDecisionAlternative>(),
            new Dictionary<string, string>()));
        buf.RecordEvent(matchId, FakeEvent("PhaseChangedEvent"));

        var result = await svc.GetReplayAsync("stub-alice", matchId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.MatchId.Should().Be(matchId);
        dto.EntryCount.Should().Be(3);
        dto.Entries.Select(e => e.Kind).Should().Equal(
            ReplayEntry.KindEvent, ReplayEntry.KindBotDecision, ReplayEntry.KindEvent);
    }

    [Fact]
    public async Task GetReplay_OpponentParty_AllowedToo()
    {
        // Both seated players may download the replay — the only access
        // check is "isParty", same as GetMatch on invite matches.
        var (svc, buf, matchId) = await NewMatchAsync();
        buf.RecordEvent(matchId, FakeEvent("TurnStartedEvent"));
        var result = await svc.GetReplayAsync("stub-bob", matchId, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value!.EntryCount.Should().Be(1);
    }

    [Fact]
    public async Task GetReplay_NoBufferForMatch_NotFound()
    {
        // Match exists, but the replay buffer is empty (e.g. a non-bot
        // match where the engine never started). Surface as match-not-found
        // rather than a 200 with zero entries — the downloader can retry.
        var (svc, _, matchId) = await NewMatchAsync();
        var result = await svc.GetReplayAsync("stub-alice", matchId, CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("match-not-found");
    }
}
