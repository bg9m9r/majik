using FluentAssertions;
using Majik.Core.Api;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchServiceEngineErrorTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceEngineErrorTests(TestMongoFixture fixture) => _fixture = fixture;

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
    /// Mirrors MatchServiceConcedeAbandonTests.NewServiceAndPlayingMatchAsync.
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
            pub, timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask), gameFactory: null,
            allowMissingDeckPlumbing: true);

        var created = await svc.CreateAsync("stub-alice",
            new CreateMatchRequest("constructed", "public", "burn", 20),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        await svc.JoinAsync("stub-bob", matchId, new JoinMatchRequest("stompy"), CancellationToken.None);
        await svc.SubmitRollAsync("stub-alice", matchId, CancellationToken.None);
        await svc.SubmitRollAsync("stub-bob", matchId, CancellationToken.None);
        await svc.PlayDrawAsync("stub-alice", matchId, new PlayDrawRequest("play"), CancellationToken.None);

        pub.Published.Clear();
        return (svc, pub, matchId);
    }

    [Fact]
    public async Task OnEngineErrorAsync_FromPlaying_TransitionsErrored_AndNotifiesClient()
    {
        var (svc, pub, matchId) = await NewServiceAndPlayingMatchAsync();

        var fault = new InvalidOperationException("boom-secret");
        await svc.OnEngineErrorAsync(matchId, EngineFaultReason.Fault, fault, CancellationToken.None);

        var get = await svc.GetAsync("stub-alice", matchId, CancellationToken.None);
        get.IsSuccess.Should().BeTrue();
        get.Value!.State.Should().Be("Errored");

        pub.Published.Should().Contain(e => e.@event == "match.engine-error");
        pub.Published.Should().Contain(e => e.@event == "match.state-changed");

        // No published payload may carry the exception message (info-leak posture).
        foreach (var (_, _, payload) in pub.Published)
        {
            payload.ToString().Should().NotContain("boom-secret");
            foreach (var prop in payload.GetType().GetProperties())
            {
                var val = prop.GetValue(payload)?.ToString();
                if (val != null) val.Should().NotContain("boom-secret");
            }
        }
    }

    [Fact]
    public async Task OnEngineErrorAsync_WhenAlreadyCompleted_IsNoOp()
    {
        var (svc, pub, matchId) = await NewServiceAndPlayingMatchAsync();

        // Terminate naturally first via concede (-> Completed).
        var concede = await svc.ConcedeAsync("stub-alice", matchId, CancellationToken.None);
        concede.IsSuccess.Should().BeTrue();
        concede.Value!.State.Should().Be("Completed");

        pub.Published.Clear();

        await svc.OnEngineErrorAsync(matchId, EngineFaultReason.Fault,
            new InvalidOperationException("boom-secret"), CancellationToken.None);

        var get = await svc.GetAsync("stub-alice", matchId, CancellationToken.None);
        get.Value!.State.Should().Be("Completed");

        pub.Published.Should().NotContain(e => e.@event == "match.state-changed");
        pub.Published.Should().NotContain(e => e.@event == "match.engine-error");
    }

    [Theory]
    [InlineData(EngineFaultReason.Fault, "engine-fault")]
    [InlineData(EngineFaultReason.Hang, "engine-hang")]
    public async Task OnEngineErrorAsync_MapsReasonOnWire(EngineFaultReason reason, string expectedWire)
    {
        var (svc, pub, matchId) = await NewServiceAndPlayingMatchAsync();

        await svc.OnEngineErrorAsync(matchId, reason, fault: null, CancellationToken.None);

        var engineError = pub.Published.Single(e => e.@event == "match.engine-error").payload;
        var reasonProp = engineError.GetType().GetProperty("reason");
        reasonProp.Should().NotBeNull();
        reasonProp!.GetValue(engineError).Should().Be(expectedWire);
    }
}
