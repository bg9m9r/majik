using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Unit tests for the vs-Bot match lifecycle. Pins the behavior that bot
/// matches flow through the Rolling state (so the dice roll is visible to
/// the user on the frontend), instead of the old "skip Rolling" shortcut.
///
/// <para>Uses <see cref="ImmediateBotMatchScheduler"/> so the bot's
/// SubmitRollAsync + (conditional) PlayDrawAsync fire synchronously
/// inside the originating call — assertions can run immediately after
/// <see cref="MatchService.CreateAsync"/> / <see cref="MatchService.SubmitRollAsync"/>
/// return.</para>
/// </summary>
public class MatchServiceBotFlowTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchServiceBotFlowTests(TestMongoFixture fixture) => _fixture = fixture;

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

    private async Task<(MatchService svc, CapturePublisher pub, ImmediateBotMatchScheduler scheduler)>
        NewServiceAsync(StubRandomSource rng, string aliceSub = "u-alice")
    {
        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile
        {
            Sub = aliceSub,
            Handle = "alice",
            HandleDisplay = "Alice",
            CreatedAt = now,
            UpdatedAt = now,
        }, CancellationToken.None);

        var pub = new CapturePublisher();
        var scheduler = new ImmediateBotMatchScheduler();
        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(rng), new StubDeckLoader(), new SystemClock(),
            pub, timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask),
            gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy(),
            botScheduler: scheduler);
        scheduler.Bind(svc);
        return (svc, pub, scheduler);
    }

    private static string BotArchetype() => Majik.Bot.Decks.BotDeckCatalog.Archetypes.First();

    // -----------------------------------------------------------------------
    // Lifecycle: Open → Joined → Starting → Rolling (no skip!)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateBotMatch_TransitionsThroughRollingNotPlaying()
    {
        // Bot rolls 1 (loses), human hasn't rolled yet — so on return,
        // state must still be Rolling.
        var (svc, pub, _) = await NewServiceAsync(new StubRandomSource(1));

        var r = await svc.CreateAsync("u-alice",
            new CreateMatchRequest("constructed", "invite", "starter", 20,
                BotOpponent: new BotOpponentRequest(BotArchetype())),
            CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.State.Should().Be("Rolling",
            "bot matches no longer skip Rolling — the dice roll has to be " +
            "visible to the user before play starts");

        // The state-changed publisher events should include each step of
        // the lifecycle. Order is: Joined → Starting → Rolling.
        var states = pub.Published
            .Where(p => p.@event == "match.state-changed")
            .Select(p =>
            {
                var prop = p.payload.GetType().GetProperty("state")!;
                return (string)prop.GetValue(p.payload)!;
            })
            .ToList();
        states.Should().ContainInOrder("Joined", "Starting", "Rolling");
        states.Should().NotContain("Playing", "match should not have entered Playing yet");
    }

    // -----------------------------------------------------------------------
    // Bot wins the roll → scheduler auto-chooses play → Playing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BotWinsRoll_AutoChoosesPlayAndAdvancesToPlaying()
    {
        // RNG draw order:
        //   1. Bot scheduler fires SubmitRollAsync(bot) during CreateAsync → bot rolls 6
        //   2. Alice posts SubmitRollAsync → she rolls 1
        // Bot wins (6 > 1), scheduler synchronously fires PlayDraw("play").
        var (svc, _, _) = await NewServiceAsync(new StubRandomSource(6, 1));

        var created = await svc.CreateAsync("u-alice",
            new CreateMatchRequest("constructed", "invite", "starter", 20,
                BotOpponent: new BotOpponentRequest(BotArchetype())),
            CancellationToken.None);
        created.Value!.State.Should().Be("Rolling");
        var matchId = created.Value.Id;

        // Alice rolls — completes both slots, bot wins, bot scheduler
        // immediately fires PlayDraw → Playing.
        var afterRoll = await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);
        afterRoll.IsSuccess.Should().BeTrue();
        afterRoll.Value!.State.Should().Be("Playing",
            "bot won the roll, so the scheduler synchronously chose 'play' " +
            "and the match transitioned into Playing inline");
        afterRoll.Value.FirstChoice.Should().Be("play");
        afterRoll.Value.Roll!.WinnerSub.Should().StartWith("bot:");
    }

    // -----------------------------------------------------------------------
    // Human wins the roll → match sits in Rolling until human chooses
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HumanWinsRoll_MatchSitsInRollingUntilHumanChoosesPlayDraw()
    {
        // Bot rolls 1, alice rolls 6 → alice wins; bot scheduler does NOT
        // schedule PlayDraw.
        var (svc, _, _) = await NewServiceAsync(new StubRandomSource(1, 6));

        var created = await svc.CreateAsync("u-alice",
            new CreateMatchRequest("constructed", "invite", "starter", 20,
                BotOpponent: new BotOpponentRequest(BotArchetype())),
            CancellationToken.None);
        var matchId = created.Value!.Id;

        var afterRoll = await svc.SubmitRollAsync("u-alice", matchId, CancellationToken.None);
        afterRoll.Value!.Roll!.WinnerSub.Should().Be("u-alice");
        afterRoll.Value.State.Should().Be("Rolling",
            "human won — match must wait for the human's play/draw choice");

        var afterPd = await svc.PlayDrawAsync("u-alice", matchId,
            new PlayDrawRequest("draw"), CancellationToken.None);
        afterPd.Value!.State.Should().Be("Playing");
        afterPd.Value.FirstChoice.Should().Be("draw");
    }

    // -----------------------------------------------------------------------
    // SignalR events: both player-rolled and rolled fire during the bot path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BotPath_FiresPlayerRolledAndRolledSignalREvents()
    {
        var (svc, pub, _) = await NewServiceAsync(new StubRandomSource(6, 1)); // bot=6, alice=1

        var created = await svc.CreateAsync("u-alice",
            new CreateMatchRequest("constructed", "invite", "starter", 20,
                BotOpponent: new BotOpponentRequest(BotArchetype())),
            CancellationToken.None);
        // Per-player roll event for the bot fired during CreateAsync.
        pub.Published.Should().Contain(p => p.@event == "match.player-rolled",
            "bot's roll must publish the same per-player event humans do — " +
            "the frontend hooks this to render the dice value");

        await svc.SubmitRollAsync("u-alice", created.Value!.Id, CancellationToken.None);

        pub.Published.Count(p => p.@event == "match.player-rolled").Should().Be(2,
            "one player-rolled event per player (bot + alice)");
        pub.Published.Should().Contain(p => p.@event == "match.rolled",
            "consolidated rolled event must fire once both slots are filled");
    }

    // -----------------------------------------------------------------------
    // Regression guard: human-vs-human still works (no bot scheduler triggered)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HumanVsHumanFlow_DoesNotTriggerBotScheduler()
    {
        var triggered = new List<string>();
        var captureScheduler = new CapturingBotMatchScheduler(triggered);

        var db = _fixture.NewDatabase();
        var matchRepo = new MatchRepository(db);
        await matchRepo.EnsureIndexesAsync(CancellationToken.None);
        var profileRepo = new UserProfileRepository(db);
        await profileRepo.EnsureIndexesAsync(CancellationToken.None);
        var now = DateTime.UtcNow;
        await profileRepo.UpsertAsync(new UserProfile
        {
            Sub = "u-alice",
            Handle = "alice",
            HandleDisplay = "Alice",
            CreatedAt = now,
            UpdatedAt = now,
        }, CancellationToken.None);
        await profileRepo.UpsertAsync(new UserProfile
        {
            Sub = "u-bob",
            Handle = "bob",
            HandleDisplay = "Bob",
            CreatedAt = now,
            UpdatedAt = now,
        }, CancellationToken.None);

        var svc = new MatchService(matchRepo, profileRepo,
            new DiceRoller(new StubRandomSource(6, 2)),
            new StubDeckLoader(), new SystemClock(),
            hub: null,
            timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask),
            gameFactory: null,
            deckOwnershipPolicy: new AllowStubDeckOwnershipPolicy(),
            botScheduler: captureScheduler);

        var created = await svc.CreateAsync("u-alice",
            new CreateMatchRequest("constructed", "public", "starter", 20),
            CancellationToken.None);
        await svc.JoinAsync("u-bob", created.Value!.Id,
            new JoinMatchRequest("starter"), CancellationToken.None);
        await svc.SubmitRollAsync("u-alice", created.Value.Id, CancellationToken.None);
        await svc.SubmitRollAsync("u-bob", created.Value.Id, CancellationToken.None);

        // No bot involvement anywhere in this flow.
        triggered.Should().BeEmpty(
            "human-vs-human matches must not invoke any bot-scheduler callbacks");
    }

    private sealed class CapturingBotMatchScheduler : IBotMatchScheduler
    {
        private readonly List<string> _events;
        public CapturingBotMatchScheduler(List<string> events) => _events = events;
        public void ScheduleBotRoll(Guid matchId, string botSub) => _events.Add($"roll:{botSub}");
        public void ScheduleBotPlayDraw(Guid matchId, string botSub) => _events.Add($"playdraw:{botSub}");
    }

    // -----------------------------------------------------------------------
    // Delay-test: ImmediateBotMatchScheduler (zero delay) completes the full
    // bot flow synchronously inside CreateAsync + SubmitRollAsync. This is
    // the test path that proves the IBotMatchScheduler seam works.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ImmediateBotScheduler_CompletesFullBotFlowSynchronously()
    {
        // Bot=6, alice=1: bot wins → ImmediateBotMatchScheduler runs both
        // SubmitRollAsync(bot) (in CreateAsync) and PlayDrawAsync(bot)
        // (in SubmitRollAsync after winner is determined) on the calling
        // thread. By the time SubmitRollAsync returns, the match is Playing.
        var (svc, _, _) = await NewServiceAsync(new StubRandomSource(6, 1));

        var created = await svc.CreateAsync("u-alice",
            new CreateMatchRequest("constructed", "invite", "starter", 20,
                BotOpponent: new BotOpponentRequest(BotArchetype())),
            CancellationToken.None);
        var afterRoll = await svc.SubmitRollAsync("u-alice", created.Value!.Id, CancellationToken.None);

        // No polling, no waits — assertion fires immediately.
        afterRoll.Value!.State.Should().Be("Playing");
    }
}
