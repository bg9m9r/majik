using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchServiceJoinTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceJoinTests(TestMongoFixture fixture) => _fixture = fixture;

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

    private async Task<(MatchService svc, CapturePublisher pub, Guid matchId)> NewServiceAndMatchAsync(StubRandomSource rng)
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
        return (svc, pub, created.Value!.Id);
    }

    [Fact]
    public async Task JoinAsync_TransitionsThroughStates_PopulatesRoll()
    {
        var rng = new StubRandomSource(4, 6); // bob wins
        var (svc, pub, matchId) = await NewServiceAndMatchAsync(rng);

        var result = await svc.JoinAsync("stub-bob",
            matchId,
            new JoinMatchRequest("stompy"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.State.Should().Be("Rolling");
        dto.Opponent.Should().NotBeNull();
        dto.Opponent!.Sub.Should().Be("stub-bob");
        dto.Roll.Should().NotBeNull();
        dto.Roll!.WinnerSub.Should().Be("stub-bob");

        // Events published in order
        pub.Published.Select(e => e.@event).Should().ContainInOrder(
            "match.opponent-joined",
            "match.state-changed",
            "match.state-changed",
            "match.rolled");
    }

    [Fact]
    public async Task JoinAsync_SelfJoin_Returns409()
    {
        var rng = new StubRandomSource(1, 6);
        var (svc, _, matchId) = await NewServiceAndMatchAsync(rng);

        var result = await svc.JoinAsync("stub-alice", matchId,
            new JoinMatchRequest("burn"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("self-join-forbidden");
    }

    [Fact]
    public async Task JoinAsync_NoSuchMatch_Returns404()
    {
        var (svc, _, _) = await NewServiceAndMatchAsync(new StubRandomSource(1, 6));

        var result = await svc.JoinAsync("stub-bob", Guid.NewGuid(),
            new JoinMatchRequest("stompy"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Error.Should().Be("match-not-found");
    }

    [Fact]
    public async Task JoinAsync_AlreadyJoined_Returns409()
    {
        var rng = new StubRandomSource(2, 5, 1, 6);
        var (svc, _, matchId) = await NewServiceAndMatchAsync(rng);

        await svc.JoinAsync("stub-bob", matchId, new JoinMatchRequest("stompy"), CancellationToken.None);
        var second = await svc.JoinAsync("stub-bob", matchId, new JoinMatchRequest("stompy"), CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.Error!.Error.Should().Be("match-not-open");
    }
}
