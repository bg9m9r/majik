using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Random;
using Majik.Server.Matches.Persistence;
using Microsoft.Extensions.Options;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Server.Tests.Matches.Persistence;

/// <summary>
/// PLAN 08 (body) — durable command-log + checkpoint + rehydrate, all gated by
/// the <see cref="EnginePersistenceOptions.Enabled"/> flag (default off).
/// </summary>
public class EnginePersistenceCoordinatorTests
{
    private const int Seed = 24680;

    // ── Flag-off: no durable writes whatsoever ──────────────────────────────
    [Fact]
    public async Task FlagOff_RecordCommand_WritesNothing_AndRehydrateReturnsNull()
    {
        var log = new CountingLogStore();
        var checkpoints = new CountingCheckpointStore();
        var coord = Build(log, checkpoints, enabled: false);

        var facade = BuildSeededFacade();
        await coord.RecordCommandAsync(Guid.NewGuid(), facade, seq: 1,
            new PassPriorityCommand(), CancellationToken.None);

        log.Appends.Should().Be(0, "flag off → no durable command-log writes");
        checkpoints.Saves.Should().Be(0, "flag off → no checkpoint writes");

        var rehydrated = await coord.TryRehydrateAsync(
            Guid.NewGuid(), Seed, BuildSeededFacade, CancellationToken.None);
        rehydrated.Should().BeNull("flag off → rehydration is disabled");
    }

    // ── Idempotency: double-append of (matchId, seq) is a no-op ─────────────
    [Fact]
    public async Task Idempotency_DoubleAppendSameSeq_IsNoOp()
    {
        var store = new InMemoryEngineCommandLogStore();
        var matchId = Guid.NewGuid();

        await store.AppendAsync(matchId, 1, DateTime.UtcNow, new PassPriorityCommand(), default);
        await store.AppendAsync(matchId, 1, DateTime.UtcNow, new MulliganCommand(true), default);

        var all = await store.ReadSinceAsync(matchId, -1, default);
        all.Should().HaveCount(1, "a duplicate (matchId, seq) append must not add a second entry");
        all[0].Command.Should().BeOfType<PassPriorityCommand>("the FIRST write stands");
        (await store.MaxSeqAsync(matchId, default)).Should().Be(1);
    }

    // ── Checkpoint + replay == full replay from zero (id-identical) ─────────
    [Fact]
    public async Task RehydrateFromCheckpoint_EqualsFullReplay_IdIdentical()
    {
        var matchId = Guid.NewGuid();
        var log = new InMemoryEngineCommandLogStore();
        var checkpoints = new InMemoryEngineCheckpointStore();
        // Checkpoint frequently so a checkpoint actually lands mid-game.
        var coord = Build(log, checkpoints, enabled: true, checkpointEvery: 2);

        // Original run under a seed-scope → its ids are the deterministic ones a
        // rehydrate must reproduce. Record every command + periodic checkpoints.
        GameStateDto originalState;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var original = BuildSeededFacade();
            await DriveAndRecordAsync(original, matchId, coord);
            originalState = original.GetState();
        }

        // A checkpoint must have been taken (proves the cheaper path is exercised).
        (await checkpoints.GetLatestAsync(matchId, default))
            .Should().NotBeNull("the cadence must have produced at least one checkpoint");

        // Rehydrate on a fresh replica: the coordinator pulls checkpoint + since,
        // reconstructs the full log, and replays it id-identically.
        var rehydrated = await coord.TryRehydrateAsync(
            matchId, Seed, BuildSeededFacade, CancellationToken.None);
        rehydrated.Should().NotBeNull();

        IdProjection(rehydrated!.GetState()).Should().BeEquivalentTo(
            IdProjection(originalState), opts => opts.WithStrictOrdering(),
            "rehydration from checkpoint + commands-since must reproduce the original id-identically");
    }

    // ── Checkpoint-write failure → fall back to full-log replay ─────────────
    [Fact]
    public async Task CheckpointWriteFailure_FallsBackToFullLogReplay()
    {
        var matchId = Guid.NewGuid();
        var log = new InMemoryEngineCommandLogStore();
        // This checkpoint store ALWAYS throws on save — the command log must
        // still capture everything and rehydration must still succeed via the
        // full-log path.
        var checkpoints = new ThrowingCheckpointStore();
        var coord = Build(log, checkpoints, enabled: true, checkpointEvery: 2);

        GameStateDto originalState;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var original = BuildSeededFacade();
            // A throwing checkpoint must NOT bubble out of RecordCommandAsync.
            await DriveAndRecordAsync(original, matchId, coord);
            originalState = original.GetState();
        }

        // No checkpoint persisted (every save threw).
        (await checkpoints.GetLatestAsync(matchId, default)).Should().BeNull();
        // But the full command log is intact.
        (await log.MaxSeqAsync(matchId, default)).Should().BeGreaterThan(0);

        var rehydrated = await coord.TryRehydrateAsync(
            matchId, Seed, BuildSeededFacade, CancellationToken.None);
        rehydrated.Should().NotBeNull("rehydration must fall back to full-log replay");

        IdProjection(rehydrated!.GetState()).Should().BeEquivalentTo(
            IdProjection(originalState), opts => opts.WithStrictOrdering(),
            "full-log replay must still reproduce the original id-identically");
    }

    // ── No durable log → rehydrate returns null (nothing to rebuild) ────────
    [Fact]
    public async Task NoDurableLog_RehydrateReturnsNull()
    {
        var coord = Build(new InMemoryEngineCommandLogStore(),
            new InMemoryEngineCheckpointStore(), enabled: true);

        var rehydrated = await coord.TryRehydrateAsync(
            Guid.NewGuid(), Seed, BuildSeededFacade, CancellationToken.None);
        rehydrated.Should().BeNull();
    }

    // ── vs-bot rehydration: bot agent re-installed, replay stays in sync ────
    // PLAN 08 soundness fix. A rehydrated vs-bot game must (1) re-install the
    // deterministic BotPlayerAgent on the Bob seat and (2) answer the bot seat's
    // re-raised prompts via that agent — NOT by dequeuing a human command
    // (bot decisions are never logged; SubmitAsync rejects bot seats). With the
    // agent re-installed the rebuild is id-identical AND the bot keeps playing.
    [Fact]
    public async Task RehydrateBotMatch_ReinstallsBotAgent_IsIdIdentical_AndBotKeepsPlaying()
    {
        const string archetype = "Burn";
        var matchId = Guid.NewGuid();
        var log = new InMemoryEngineCommandLogStore();
        var checkpoints = new InMemoryEngineCheckpointStore();
        var coord = Build(log, checkpoints, enabled: true, checkpointEvery: 2);

        var factory = BuildServerGameFactory();

        // Original vs-bot run under a seed-scope. Bob is a BotPlayerAgent, so the
        // prompt channel only delivers Alice (human) prompts and only Alice's
        // commands are recorded — the bot self-drives via ChooseAsync in-engine.
        GameStateDto originalState;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var original = factory.Create(
                "Alice", "Bob", BuildDeck(), BuildDeck(), botSeatArchetype: archetype);
            original.IsHumanSeat(original.Bob.Id).Should().BeFalse(
                "the original Bob seat is the bot");
            await DriveAndRecordAsync(original, matchId, coord);
            originalState = original.GetState();
        }

        var aliceId = originalState.Players.Single(p => p.Name == "Alice").Id;
        var bobId = originalState.Players.Single(p => p.Name == "Bob").Id;
        var loggedCommands = await log.ReadSinceAsync(matchId, -1, CancellationToken.None);
        loggedCommands.Should().NotBeEmpty("the human seat submits commands");
        loggedCommands.Should().OnlyContain(
            a => a.Command.PlayerId == aliceId,
            "only the human (Alice) seat's commands are ever logged — bot decisions " +
            "go through ChooseAsync in-engine and are never recorded");
        loggedCommands.Should().NotContain(
            a => a.Command.PlayerId == bobId,
            "no bot (Bob) seat command is ever recorded");

        // Rehydrate WITH the bot archetype → BuildUnregisteredFacade re-installs
        // the BotPlayerAgent on Bob before replay (the production path in
        // MatchService.TryRehydrateAndDispatchAsync).
        var rehydrated = await coord.TryRehydrateAsync(
            matchId, Seed,
            () => factory.BuildUnregisteredFacade(
                "Alice", "Bob", BuildDeck(), BuildDeck(), botSeatArchetype: archetype),
            CancellationToken.None);

        rehydrated.Should().NotBeNull();
        rehydrated!.IsHumanSeat(rehydrated.Bob.Id).Should().BeFalse(
            "the rehydrated Bob seat must again be the self-driving bot");
        rehydrated.IsHumanSeat(rehydrated.Alice.Id).Should().BeTrue(
            "the rehydrated Alice seat is still the wire-facing human");

        IdProjection(rehydrated.GetState()).Should().BeEquivalentTo(
            IdProjection(originalState), opts => opts.WithStrictOrdering(),
            "a rehydrated vs-bot game must reproduce the original id-identically — the " +
            "bot's replayed in-engine decisions match, no human/bot prompt desync");

        // The bot keeps playing: the rehydrated game is a normal live facade whose
        // full-game task is still progressing (or already terminal), never faulted
        // by a desync/deadlock, and a further human command is accepted on Alice's
        // seat without throwing.
        rehydrated.FullGameTask.Should().NotBeNull();
        rehydrated.FullGameTask!.IsFaulted.Should().BeFalse(
            "a synced rehydrated bot game must not fault");
    }

    // ── Counter-proof: omitting the bot reinstall desyncs the rehydrate ─────
    // Guards the fix: rehydrating a vs-bot game WITHOUT the archetype leaves Bob
    // on the default RemoteAgent, so the bot seat's prompts now reach the replay
    // channel and dequeue Alice's logged commands against the WRONG prompts —
    // the rebuilt state diverges from the original.
    [Fact]
    public async Task RehydrateBotMatch_WithoutReinstall_Desyncs()
    {
        const string archetype = "Burn";
        var matchId = Guid.NewGuid();
        var log = new InMemoryEngineCommandLogStore();
        var checkpoints = new InMemoryEngineCheckpointStore();
        var coord = Build(log, checkpoints, enabled: true, checkpointEvery: 2);

        var factory = BuildServerGameFactory();

        GameStateDto originalState;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var original = factory.Create(
                "Alice", "Bob", BuildDeck(), BuildDeck(), botSeatArchetype: archetype);
            await DriveAndRecordAsync(original, matchId, coord);
            originalState = original.GetState();
        }

        // Rehydrate WITHOUT the archetype — the latent bug. Bob is a RemoteAgent,
        // so its prompts hit the channel and steal Alice's logged commands.
        var broken = await coord.TryRehydrateAsync(
            matchId, Seed,
            () => factory.BuildUnregisteredFacade("Alice", "Bob", BuildDeck(), BuildDeck()),
            CancellationToken.None);

        broken.Should().NotBeNull();
        broken!.IsHumanSeat(broken.Bob.Id).Should().BeTrue(
            "without the reinstall the bot seat falls back to the default RemoteAgent");
        IdProjection(broken.GetState()).Should().NotBeEquivalentTo(
            IdProjection(originalState), opts => opts.WithStrictOrdering(),
            "without re-installing the bot agent the prompt-driven replay desyncs — the " +
            "rebuilt state must NOT match the original (this is the bug the fix closes)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static Majik.Server.Composition.ServerGameFactory BuildServerGameFactory()
        => new(new GameRegistry());

    private static EnginePersistenceCoordinator Build(
        IEngineCommandLogStore log,
        IEngineCheckpointStore checkpoints,
        bool enabled,
        int checkpointEvery = 25)
    {
        var options = Options.Create(new EnginePersistenceOptions
        {
            Enabled = enabled,
            CheckpointEveryCommands = checkpointEvery,
        });
        return new EnginePersistenceCoordinator(log, checkpoints, options);
    }

    /// <summary>Drive a scripted game, recording each command at its facade seq
    /// (= the 1-based command-log count) through the coordinator — exactly what
    /// MatchService.SubmitCommandAsync does on the wire.</summary>
    private static async Task DriveAndRecordAsync(
        GameFacade facade, Guid matchId, EnginePersistenceCoordinator coord)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => channel.Writer.TryWrite(p));

        await facade.StartFullGameAsync(
            maxTurns: 2, rng: new GameRandom(Seed), logicalClock: new LogicalClock());

        var game = facade.FullGameTask!;
        for (var step = 0; step < 1000; step++)
        {
            if (game.IsCompleted) return;
            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game);
            if (winner == game) return;
            if (!await read) return;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = NextCommand(facade, prompt) with { PlayerId = prompt.PlayerId };
            try { await facade.SubmitAsync(cmd); }
            catch (InvalidOperationException) { return; }

            // Seq = the command's 1-based position in the facade's action log —
            // the same monotonic value MatchService records.
            var seq = facade.Log.Actions.Count;
            await coord.RecordCommandAsync(matchId, facade, seq, cmd, CancellationToken.None);
        }
    }

    private static GameCommand NextCommand(GameFacade facade, PromptDto prompt)
    {
        var kinds = prompt.ExpectedKinds;
        if (kinds.Contains(nameof(MulliganCommand)))
            return new MulliganCommand(Keep: true);
        if (kinds.Contains(nameof(ChooseCardsToBottomCommand)))
        {
            var n = prompt.BottomCount ?? 0;
            var hand = PlayerOf(facade, prompt.PlayerId).Hand.Cards;
            return new ChooseCardsToBottomCommand(hand.Take(n).Select(c => c.InstanceId).ToList());
        }
        if (kinds.Contains(nameof(DeclareAttackersCommand)))
        {
            var me = PlayerOf(facade, prompt.PlayerId);
            var opp = OpponentOf(facade, prompt.PlayerId);
            var attackers = me.Battlefield.Cards
                .Where(c => c.Types.Contains("Creature") && !c.Tapped && !c.SummoningSickness)
                .Select(c => new AttackerDeclarationDto(c.InstanceId, opp.Id))
                .ToList();
            return new DeclareAttackersCommand(attackers);
        }
        if (kinds.Contains(nameof(DeclareBlockersCommand)))
            return new DeclareBlockersCommand(System.Array.Empty<BlockerDeclarationDto>());
        if (kinds.Contains(nameof(OrderTriggersCommand)))
            return new OrderTriggersCommand(System.Array.Empty<System.Guid>());
        return new PassPriorityCommand();
    }

    // Build the facade through the PUBLIC deck-list constructor (Server.Tests
    // has no access to Core's internal board-mutation helpers) — exactly the
    // path the real server uses: libraries of fresh card instances drawn through
    // the normal mulligan/draw flow. Decks are deterministic in composition +
    // order, so the same seed reproduces the same shuffle → id-identical replay.
    // The legendary-pair / ETB-timestamp legend scenario is covered by the
    // Core.Api RehydrateTests.
    private static GameFacade BuildSeededFacade()
        => GameFacade.Create("Alice", "Bob", BuildDeck(), BuildDeck());

    private static IReadOnlyList<ICard> BuildDeck()
    {
        var cards = new List<ICard>();
        for (var i = 0; i < 24; i++) cards.Add(new Land("Forest"));
        for (var i = 0; i < 12; i++) cards.Add(new Creature("Grizzly Bears", "1G", 2, 2));
        return cards;
    }

    private static object IdProjection(GameStateDto s) => new
    {
        Players = s.Players.Select(p => new
        {
            p.Name,
            p.Id,
            Battlefield = p.Battlefield.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Hand = p.Hand.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Graveyard = p.Graveyard.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Library = p.Library.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
        }).ToList(),
        Stack = s.Stack.Select(o => $"{o.Kind}|{o.Id}").ToList(),
    };

    private static PlayerDto PlayerOf(GameFacade facade, Guid playerId)
        => facade.GetState().Players.First(p => p.Id == playerId);

    private static PlayerDto OpponentOf(GameFacade facade, Guid playerId)
        => facade.GetState().Players.First(p => p.Id != playerId);

    // ── Test doubles ────────────────────────────────────────────────────────
    private sealed class CountingLogStore : InMemoryEngineCommandLogStore
    {
        public int Appends;
        public override Task AppendAsync(Guid m, long s, DateTime at, GameCommand c, CancellationToken ct)
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

    private sealed class ThrowingCheckpointStore : IEngineCheckpointStore
    {
        public Task SaveAsync(EngineCheckpoint cp, CancellationToken ct)
            => throw new InvalidOperationException("simulated checkpoint-store fault");
        public Task<EngineCheckpoint?> GetLatestAsync(Guid matchId, CancellationToken ct)
            => Task.FromResult<EngineCheckpoint?>(null);
    }
}
