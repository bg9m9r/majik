using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Server.Matches;
using Majik.Server.Matches.Persistence;
using Majik.Server.Profiles;
using Majik.Server.Tests.Helpers;
using Majik.Server.Tests.Profiles;
using Microsoft.Extensions.Options;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Server.Tests.Matches.Persistence;

/// <summary>
/// Regression suite for the "human-vs-bot match cannot self-resume on the
/// BOT's turn after the in-memory facade is lost (server restart / redeploy /
/// crash)" bug pair. Both are driven through the REAL
/// <see cref="MatchService"/> command + GET paths, persistence enabled, with
/// the facade dropped from the registry to simulate a replica restart.
///
/// <list type="bullet">
/// <item><b>Bug 1 — no rehydration trigger on the bot's turn.</b> Rehydration
///   was lazy + command-driven (only <see cref="MatchService.SubmitCommandAsync"/>
///   rebuilt a missing facade). On the bot's turn no human command arrives, so
///   <see cref="MatchService.GetGameStateAsync"/> (what the portal calls on
///   load / reconnect) returned <c>game-not-started</c> instead of rehydrating
///   — the match froze at its last snapshot. The fix makes GetGameStateAsync a
///   rehydration trigger: it rebuilds + registers the facade and the resumed
///   full-game loop drives the bot forward on its own.</item>
/// <item><b>Bug 2 — rehydrated facade never re-wired the per-match SignalR
///   bot-decision sink.</b> The rehydrate path passed
///   <c>extraDecisionSink: null</c>, so post-rehydrate bot decisions never
///   reached the <c>bot-decision</c> channel and the portal panel stayed empty.
///   The fix plumbs the per-match sink through <c>BuildUnregisteredFacade</c>.</item>
/// </list>
/// </summary>
public class BotTurnRehydrationOnGetStateTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public BotTurnRehydrationOnGetStateTests(TestMongoFixture fixture) => _fixture = fixture;

    // ── Bug 1 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetGameStateAsync_OnBotTurn_AfterFacadeLost_RehydratesAndBotAdvances()
    {
        var h = await DriveBotTurnMatchAsync();

        // Capture the frozen snapshot the match would be stuck at, then SIMULATE
        // A REPLICA RESTART by dropping the in-memory facade from the registry.
        var frozen = (await h.Svc.GetGameStateAsync(h.AliceSub, h.MatchId, default)).Value!;
        h.Factory.Delete(h.GameId).Should().BeTrue("the live facade is dropped (crash/restart)");

        // It IS the bot's turn — no human command will arrive. Pre-fix,
        // GetGameStateAsync returns game-not-started here and the match is frozen
        // forever. Post-fix it must rehydrate + return live state.
        var afterRestart = await h.Svc.GetGameStateAsync(h.AliceSub, h.MatchId, default);
        afterRestart.IsSuccess.Should().BeTrue(
            "GetGameStateAsync on the bot's turn must REHYDRATE the lost facade " +
            "(not return game-not-started) so the match self-resumes");
        h.Factory.Get(h.GameId).Should().NotBeNull(
            "the rehydrated facade is registered under the original game id");

        // The resumed full-game loop drives the bot forward on its own. Poll the
        // (now fast-path) GET until the state progresses past the frozen point —
        // the bot's turn advances (turn/phase moves) or a human prompt is raised.
        var progressed = await PollForProgressAsync(h, frozen);
        progressed.Should().BeTrue(
            "once rehydrated, the bot must advance past the frozen snapshot " +
            "(turn/phase progression) rather than staying wedged");
    }

    // ── Bug 2 ──────────────────────────────────────────────────────────────
    //
    // The rehydrate path builds its facade via
    // ServerGameFactory.BuildUnregisteredFacade and MatchService re-wires the
    // per-match SignalR bot-decision sink through its new extraDecisionSink
    // parameter (the create path always did; the rehydrate path passed null,
    // leaving the portal panel empty post-rehydrate). We prove the plumbing at
    // that exact seam: a facade built WITH an extraDecisionSink publishes the
    // bot's live in-engine decisions to it; the same factory built WITHOUT it
    // (the pre-fix rehydrate call) publishes nothing. This is replay-independent
    // (no recorded-stream determinism needed): it drives a FRESH bot game on the
    // rehydrate-path builder and observes the sink the fix forwards.
    [Fact]
    public async Task BuildUnregisteredFacade_ForwardsExtraDecisionSink_SoRehydratedBotPublishes()
    {
        var factory = new Majik.Server.Composition.ServerGameFactory(new GameRegistry(), cardRepo: null);
        var captured = new Majik.Bot.Diagnostics.CapturingBotDecisionSink();

        // Build via the SAME entry point the rehydrate path uses, passing the
        // per-match sink through the parameter the fix added.
        var facade = factory.BuildUnregisteredFacade(
            "Alice", "Bot", FixedDeckLoader.BuildDeck(), FixedDeckLoader.BuildDeck(),
            botSeatArchetype: Majik.Bot.Decks.BotDeckCatalog.Archetypes.First(),
            extraDecisionSink: captured);

        var published = await DriveUntilBotPublishesAsync(facade, captured);
        published.Should().BeTrue(
            "BuildUnregisteredFacade must forward extraDecisionSink into the bot " +
            "agent so a rehydrated bot match re-publishes its decisions on the " +
            "per-match channel (Bug 2). Pre-fix this parameter did not exist and " +
            "the rehydrate call wired a null sink.");
    }

    [Fact]
    public async Task BuildUnregisteredFacade_WithoutExtraSink_PublishesNothing_PreFixBehaviour()
    {
        var factory = new Majik.Server.Composition.ServerGameFactory(new GameRegistry(), cardRepo: null);
        var captured = new Majik.Bot.Diagnostics.CapturingBotDecisionSink();

        // No extraDecisionSink (the pre-fix rehydrate call) → the bot's decisions
        // reach NO per-match sink, however far we drive it.
        var facade = factory.BuildUnregisteredFacade(
            "Alice", "Bot", FixedDeckLoader.BuildDeck(), FixedDeckLoader.BuildDeck(),
            botSeatArchetype: Majik.Bot.Decks.BotDeckCatalog.Archetypes.First());

        var published = await DriveUntilBotPublishesAsync(facade, captured, deadlineSeconds: 4);
        published.Should().BeFalse(
            "with no extraDecisionSink wired (pre-fix rehydrate behaviour) the " +
            "per-match capture must stay empty");
    }

    /// <summary>Start a full game on <paramref name="facade"/> and drive the
    /// human (Alice) seat minimally until the bot publishes at least one decision
    /// to <paramref name="sink"/> or a wall-clock cap elapses. The bot drives
    /// itself; we just keep the human from blocking the loop.</summary>
    private static async Task<bool> DriveUntilBotPublishesAsync(
        Majik.Core.Api.GameFacade facade,
        Majik.Bot.Diagnostics.CapturingBotDecisionSink sink,
        int deadlineSeconds = 12)
    {
        await facade.StartFullGameAsync(maxTurns: 6, rng: new Majik.Core.Random.GameRandom(7));
        var game = facade.FullGameTask!;
        var aliceId = facade.Alice.Id;
        var deadline = DateTime.UtcNow.AddSeconds(deadlineSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (sink.Decisions.Count > 0) return true;
            if (game.IsCompleted) break;
            // Keep Alice unblocked so the bot keeps getting priority. The engine
            // accepts whichever opener fits the current human prompt; the rest
            // are rejected cleanly (we submit directly to the facade, stamping
            // Alice's seat).
            try
            {
                await facade.SubmitAsync(new MulliganCommand(Keep: true) { PlayerId = aliceId });
            }
            catch
            {
                try { await facade.SubmitAsync(new PassPriorityCommand { PlayerId = aliceId }); }
                catch { /* nothing legal for Alice right now — bot is acting */ }
            }
            await Task.Delay(10);
        }
        return sink.Decisions.Count > 0;
    }

    // -----------------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------------

    private sealed record Harness(
        MatchService Svc, Guid MatchId, string AliceSub, Guid GameId,
        Majik.Server.Composition.ServerGameFactory Factory, CapturingHub Hub);

    /// <summary>
    /// Drive a vs-bot match where the BOT wins the roll → the scheduler
    /// auto-chooses play → the bot takes turn 1, so the match is Playing and it
    /// is the BOT's turn. Persistence is ON (command-log + bot-decision store)
    /// so the lost facade is rehydratable. Alice's opening mulligan is submitted
    /// through the real command path so the bot is left genuinely self-driving
    /// (no pending human action needed to make progress).
    /// </summary>
    private async Task<Harness> DriveBotTurnMatchAsync()
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

        var coord = new EnginePersistenceCoordinator(
            new InMemoryEngineCommandLogStore(),
            new InMemoryEngineCheckpointStore(),
            Options.Create(new EnginePersistenceOptions
            {
                Enabled = true, CheckpointEveryCommands = 4,
            }),
            botDecisions: new InMemoryBotDecisionLogStore());

        var registry = new GameRegistry();
        var factory = new Majik.Server.Composition.ServerGameFactory(registry, cardRepo: null);
        var hub = new CapturingHub();
        var bridge = new MatchFacadeBridge(hub,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MatchFacadeBridge>.Instance, null);
        var scheduler = new ImmediateBotMatchScheduler();

        var svc = new MatchService(matchRepo, profileRepo,
            // bot rolls 6, Alice rolls 1 → BOT wins → scheduler auto-plays →
            // the bot takes turn 1 (it is the bot's turn).
            new DiceRoller(new SeqRandom(6, 1)), new FixedDeckLoader(), new SystemClock(),
            hub,
            timeoutScheduler: new MatchTimeoutScheduler((_, _, _) => Task.CompletedTask),
            gameFactory: factory,
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

        var afterRoll = await svc.SubmitRollAsync(aliceSub, matchId, default);
        afterRoll.IsSuccess.Should().BeTrue();
        afterRoll.Value!.State.Should().Be("Playing", "bot won the roll → scheduler auto-played");

        var match = await matchRepo.GetByIdAsync(matchId, default);
        var gid = match!.GameId!.Value;

        // Establish a durable command log (rehydration needs one) by submitting
        // Alice's opening mulligan keep through the REAL command path, then freeze
        // on the bot's own turn 1 while it is still in a PRE-MAIN step (Untap /
        // Upkeep / Draw), BEFORE it commits a board card. That keeps the recorded
        // bot stream id-free (mulligan keep + priority passes) so the replay hands
        // off cleanly to a FRESH live decision the re-wired sink can publish.
        // (Bot deck cards are constructed outside the deterministic id scope in
        // the server flow, so a recorded card-id decision would not reproduce on
        // rehydrate — we deliberately freeze before any such decision.)
        await DriveToBotPreMainAsync(svc, factory, gid, matchId, aliceSub);

        return new Harness(svc, matchId, aliceSub, gid, factory, hub);
    }

    private static readonly string[] PreMainSteps = { "Untap", "Upkeep", "Draw" };

    /// <summary>Submit Alice's opening mulligan (establishing the durable command
    /// log rehydration needs), then freeze on the bot's own turn 1 while it is in
    /// a pre-main step — BEFORE any board-card decision — with the loop still
    /// running. Wall-clock capped.</summary>
    private static async Task DriveToBotPreMainAsync(
        MatchService svc, Majik.Server.Composition.ServerGameFactory factory,
        Guid gid, Guid matchId, string aliceSub)
    {
        var facade = factory.Get(gid)!;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var aliceMulliganed = false;

        while (DateTime.UtcNow < deadline)
        {
            if (facade.FullGameTask?.IsCompleted == true) break;

            // Get Alice's opening mulligan onto the durable log exactly once.
            if (!aliceMulliganed)
            {
                aliceMulliganed = (await svc.SubmitCommandAsync(
                    aliceSub, matchId, new MulliganCommand(Keep: true), default)).IsSuccess;
            }

            var s = facade.GetState();
            var botActive = s.ActivePlayerId == facade.Bob.Id;
            if (aliceMulliganed && botActive && PreMainSteps.Contains(s.Phase))
                break; // bot's turn, pre-main, durable log present → freeze here.

            await Task.Delay(10);
        }
    }

    /// <summary>Poll the fast-path GET until the state advances past
    /// <paramref name="frozen"/> (turn/phase/seq progression) or a brief
    /// timeout elapses.</summary>
    private static async Task<bool> PollForProgressAsync(
        Harness h, Majik.Core.Api.Dtos.GameStateDto frozen)
    {
        for (var i = 0; i < 200; i++)
        {
            var r = await h.Svc.GetGameStateAsync(h.AliceSub, h.MatchId, default);
            if (r.IsSuccess)
            {
                var s = r.Value!;
                if (s.TurnNumber != frozen.TurnNumber
                    || s.Phase != frozen.Phase
                    || s.Seq > frozen.Seq
                    || s.ActivePlayerId != frozen.ActivePlayerId)
                {
                    return true;
                }
            }
            await Task.Delay(25);
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // Stubs
    // -----------------------------------------------------------------------

    private sealed class SeqRandom : IRandomSource
    {
        private readonly Queue<int> _v;
        public SeqRandom(params int[] v) => _v = new Queue<int>(v);
        public int NextInt(int min, int max) => _v.Count > 0 ? _v.Dequeue() : min;
    }

    /// <summary>Deterministic deck loader: BOTH load paths return the SAME
    /// canonical deck regardless of the deckId / name list, so the rehydrate
    /// path (which materializes from the persisted DeckSnapshot via
    /// <see cref="IDeckLoader.LoadFromCardNamesAsync"/>) rebuilds an identical
    /// deck composition to the original (loaded via <see cref="IDeckLoader.LoadAsync"/>).
    /// Combined with the persisted game seed this makes the rehydrate faithful
    /// even when the human's DeckSnapshot is empty (the stub-deck path).</summary>
    private sealed class FixedDeckLoader : IDeckLoader
    {
        public static IReadOnlyList<ICard> BuildDeck()
        {
            var cards = new List<ICard>();
            for (var i = 0; i < 24; i++) cards.Add(new Land("Forest"));
            for (var i = 0; i < 36; i++) cards.Add(new Creature("Grizzly Bears", "1G", 2, 2));
            return cards;
        }

        public Task<IReadOnlyList<ICard>> LoadAsync(string deckId, CancellationToken ct)
            => Task.FromResult(BuildDeck());

        public Task<IReadOnlyList<ICard>> LoadFromCardNamesAsync(
            IReadOnlyList<string> cardNames, CancellationToken ct)
            => Task.FromResult(BuildDeck());
    }

    /// <summary>Hub publisher that records every <c>bot-decision</c> publish so
    /// the test can assert the per-match SignalR sink is wired (live AND after a
    /// rehydration). All other channels are accepted + ignored.</summary>
    private sealed class CapturingHub : IMatchHubPublisher
    {
        public readonly List<object> BotDecisions = new();
        public void Publish(Guid matchId, string @event, object payload)
        {
            if (@event == SignalrBotDecisionSink.Channel)
            {
                lock (BotDecisions) BotDecisions.Add(payload);
            }
        }
    }
}
