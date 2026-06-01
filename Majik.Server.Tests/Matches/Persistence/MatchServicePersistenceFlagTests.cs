using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Server.Matches;
using Majik.Server.Matches.Persistence;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Microsoft.Extensions.Options;
using Xunit;

namespace Majik.Server.Tests.Matches.Persistence;

/// <summary>
/// PLAN 08 (body) — end-to-end flag gating through the real
/// <see cref="MatchService.SubmitCommandAsync"/> path. Drives a vs-bot match to
/// Playing (Alice wins the roll + chooses play → takes turn 1) and submits her
/// first valid engine command (the opening mulligan keep).
///
/// <list type="bullet">
/// <item><b>Flag OFF:</b> NO durable command-log / checkpoint writes happen — the
///   server behaves exactly as today.</item>
/// <item><b>Flag ON:</b> the accepted command is durably appended at its seq.</item>
/// </list>
/// </summary>
public class MatchServicePersistenceFlagTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServicePersistenceFlagTests(TestMongoFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FlagOff_SubmitCommand_PerformsNoDurableWrites()
    {
        var (svc, matchId, alice, log, checkpoints) = await DriveBotMatchToPlayingAsync(enabled: false);

        // Alice's first valid engine command (opening mulligan) through the real
        // submit path.
        var r = await svc.SubmitCommandAsync(alice, matchId, new MulliganCommand(Keep: true), default);
        r.IsSuccess.Should().BeTrue();

        log.Appends.Should().Be(0, "flag off → the durable command log is never written");
        checkpoints.Saves.Should().Be(0, "flag off → no checkpoints are written");
    }

    [Fact]
    public async Task FlagOn_SubmitCommand_DurablyAppendsAtSeq()
    {
        var (svc, matchId, alice, log, _) = await DriveBotMatchToPlayingAsync(enabled: true);

        var before = log.Appends;
        var r = await svc.SubmitCommandAsync(alice, matchId, new MulliganCommand(Keep: true), default);
        r.IsSuccess.Should().BeTrue();

        log.Appends.Should().BeGreaterThan(before,
            "flag on → an accepted command is durably appended to the command log");
        (await log.MaxSeqAsync(matchId, default)).Should().BeGreaterThan(0,
            "the durable log carries a monotonic seq for the accepted command");
    }

    // -----------------------------------------------------------------------
    // Harness: build a real MatchService (real engine facade) + a flag-gated
    // persistence coordinator over counting in-memory stores, drive a vs-bot
    // match to Playing (Alice wins the roll + chooses play, taking turn 1).
    // -----------------------------------------------------------------------
    private async Task<(MatchService Svc, System.Guid MatchId, string AliceSub,
        CountingLogStore Log, CountingCheckpointStore Checkpoints)>
        DriveBotMatchToPlayingAsync(bool enabled)
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(default);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(default);

        const string aliceSub = "u-alice";
        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile
        {
            Sub = aliceSub, Handle = "alice", HandleDisplay = "Alice",
            CreatedAt = now, UpdatedAt = now,
        }, default);

        var log = new CountingLogStore();
        var checkpoints = new CountingCheckpointStore();
        var coord = new EnginePersistenceCoordinator(log, checkpoints,
            Options.Create(new EnginePersistenceOptions { Enabled = enabled, CheckpointEveryCommands = 5 }));

        var registry = new GameRegistry();
        var gameFactory = new Majik.Server.Composition.ServerGameFactory(registry, cardRepo: null);
        var bridge = new MatchFacadeBridge(new NullPublisher(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MatchFacadeBridge>.Instance, null);

        var scheduler = new ImmediateBotMatchScheduler();
        var svc = new MatchService(matchRepo, profileRepo,
            // Bot rolls 1, Alice rolls 6 → ALICE wins the roll so she chooses
            // play/draw (the scheduler does NOT auto-play for a human winner) and,
            // choosing "play", takes the first turn — so her opening MulliganCommand
            // is a valid action the engine accepts.
            new DiceRoller(new SeqRandom(1, 6)), new StubDeckLoader(), new SystemClock(),
            new NullPublisher(),
            timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask),
            gameFactory: gameFactory,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy(),
            facadeBridge: bridge,
            botScheduler: scheduler,
            persistence: coord);
        scheduler.Bind(svc);

        var archetype = Majik.Bot.Decks.BotDeckCatalog.Archetypes.First();
        var created = await svc.CreateAsync(aliceSub,
            new CreateMatchRequest("constructed", "invite", "starter", 20,
                BotOpponent: new BotOpponentRequest(archetype)),
            default);
        created.IsSuccess.Should().BeTrue();
        var matchId = created.Value!.Id;

        // Alice rolls 6 → she wins → match sits in Rolling awaiting her choice.
        var afterRoll = await svc.SubmitRollAsync(aliceSub, matchId, default);
        afterRoll.IsSuccess.Should().BeTrue();

        // Alice chooses "play" → she takes turn 1 → Playing + engine started.
        var afterPlay = await svc.PlayDrawAsync(aliceSub, matchId, new PlayDrawRequest("play"), default);
        afterPlay.IsSuccess.Should().BeTrue();
        afterPlay.Value!.State.Should().Be("Playing");

        return (svc, matchId, aliceSub, log, checkpoints);
    }

    private sealed class SeqRandom : IRandomSource
    {
        private readonly Queue<int> _v;
        public SeqRandom(params int[] v) => _v = new Queue<int>(v);
        public int NextInt(int min, int max) => _v.Count > 0 ? _v.Dequeue() : min;
    }

    private sealed class NullPublisher : IMatchHubPublisher
    {
        public void Publish(System.Guid matchId, string @event, object payload) { }
    }

    private sealed class CountingLogStore : InMemoryEngineCommandLogStore
    {
        public int Appends;
        public override Task AppendAsync(System.Guid m, long s, DateTime at, GameCommand c, CancellationToken ct)
        {
            Interlocked.Increment(ref Appends);
            return base.AppendAsync(m, s, at, c, ct);
        }
    }

    private sealed class CountingCheckpointStore : InMemoryEngineCheckpointStore
    {
        public int Saves;
        public override Task SaveAsync(EngineCheckpoint cp, CancellationToken ct)
        {
            Interlocked.Increment(ref Saves);
            return base.SaveAsync(cp, ct);
        }
    }
}
