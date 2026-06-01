using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Api.Tests;

/// <summary>
/// PLAN 08 (body) — <see cref="GameFacade.Rehydrate"/> is the replica-rehydration
/// entry point: given the pinned game seed + the ordered command log (the
/// commands since the last checkpoint, or the whole log when there is none) it
/// reconstructs a LIVE facade that is ID-IDENTICAL to the original (the portal
/// keys its reducer by these ids, so id-identity is required for a crashed game
/// to resume seamlessly). It owns the deterministic-id scope itself (seeded from
/// the game seed) so the caller doesn't have to, and suppresses event fan-out
/// across the replay so the historical command stream never re-reaches the wire.
/// </summary>
public class RehydrateTests
{
    private const int Seed = 7777;

    [Fact]
    public async Task Rehydrate_FromSeedAndFullLog_IsIdIdentical()
    {
        // ── Original run ────────────────────────────────────────────────
        // Build + drive the original under a seed-scope so its ids are the
        // deterministic ones the rehydrated replica must reproduce.
        GameStateDto originalState;
        IReadOnlyList<LoggedCommand> log;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var original = BuildSeededFacade();
            await DriveAsync(original);
            originalState = original.GetState();
            log = original.SaveSnapshot().Log;
        }

        log.Should().NotBeEmpty("the scenario must submit commands so there is a log to replay");

        // ── Rehydrate (replica reconstruction) ──────────────────────────
        // Rehydrate OWNS the id scope — the caller passes only (build, seed, log).
        var rehydrated = await GameFacade.Rehydrate(
            buildFreshFacade: BuildSeededFacade,
            seed: Seed,
            commandsSinceCheckpoint: log);

        IdProjection(rehydrated.GetState()).Should().BeEquivalentTo(
            IdProjection(originalState), opts => opts.WithStrictOrdering(),
            "a rehydrated replica must reproduce the original's portal-facing ids byte-for-byte");
    }

    [Fact]
    public async Task Rehydrate_SuppressesEventFanout_DuringReplay()
    {
        GameSnapshot snapshot;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var original = BuildSeededFacade();
            await DriveAsync(original);
            snapshot = original.SaveSnapshot();
        }

        var eventsDuringReplay = 0;
        var rehydrated = await GameFacade.Rehydrate(
            buildFreshFacade: BuildSeededFacade,
            seed: Seed,
            commandsSinceCheckpoint: snapshot.Log,
            // Subscriber attached the instant the fresh facade exists — proves
            // the historical events are NOT re-broadcast during the fast-forward.
            onFacadeCreated: f => f.Subscribe(_ => Interlocked.Increment(ref eventsDuringReplay)));

        eventsDuringReplay.Should().Be(0,
            "Rehydrate must suppress event fan-out across the replay window — a " +
            "rehydrated facade must not re-emit history to attached subscribers");

        // After Rehydrate the facade is a normal live facade.
        rehydrated.GetState().Should().NotBeNull();
    }

    [Fact]
    public async Task Rehydrate_FromCheckpointPlusCommandsSince_EqualsFullReplayFromZero()
    {
        // Capture an original run and split its log at an arbitrary mid-point K.
        GameStateDto fullReplayState;
        IReadOnlyList<LoggedCommand> fullLog;
        GameSnapshot checkpointSnapshot;
        IReadOnlyList<LoggedCommand> commandsSince;

        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var original = BuildSeededFacade();

            // Drive the original to completion, capturing a checkpoint snapshot
            // partway through (after K commands) — the checkpoint bundles the
            // command prefix [0..K] + the seed, exactly like the durable store.
            var (state, log, checkpoint, since) = await DriveCapturingCheckpointAsync(original);
            fullReplayState = state;
            fullLog = log;
            checkpointSnapshot = checkpoint;
            commandsSince = since;
        }

        // Full replay from zero.
        GameFacade fromZero;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            fromZero = await GameFacade.Rehydrate(BuildSeededFacade, Seed, fullLog);
        }

        // Replay reconstructed from checkpoint@K + commands(K+1..N): the
        // checkpoint's bundled prefix log + the commands-since are concatenated,
        // exactly the work the server does on a rehydrate-on-miss.
        var reconstructed = checkpointSnapshot.Log.Concat(commandsSince).ToList();
        reconstructed.Should().HaveCount(fullLog.Count,
            "checkpoint prefix + commands-since must reconstruct the whole log");

        GameFacade fromCheckpoint;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            fromCheckpoint = await GameFacade.Rehydrate(BuildSeededFacade, Seed, reconstructed);
        }

        IdProjection(fromCheckpoint.GetState()).Should().BeEquivalentTo(
            IdProjection(fromZero.GetState()), opts => opts.WithStrictOrdering(),
            "rehydration from checkpoint@K + commands(K+1..N) must equal full replay from 0");
    }

    // -----------------------------------------------------------------------
    // Drive helpers.
    // -----------------------------------------------------------------------
    private static async Task DriveAsync(GameFacade facade)
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
        }
    }

    /// <summary>
    /// Drive to completion but take a SaveSnapshot checkpoint after the Kth
    /// command, then keep driving. Returns the final state, the whole log, the
    /// checkpoint snapshot (log prefix [0..K] + seed), and the commands issued
    /// after the checkpoint (K+1..N).
    /// </summary>
    private static async Task<(GameStateDto State, IReadOnlyList<LoggedCommand> FullLog,
        GameSnapshot Checkpoint, IReadOnlyList<LoggedCommand> CommandsSince)>
        DriveCapturingCheckpointAsync(GameFacade facade)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => channel.Writer.TryWrite(p));

        await facade.StartFullGameAsync(
            maxTurns: 2, rng: new GameRandom(Seed), logicalClock: new LogicalClock());

        var game = facade.FullGameTask!;
        GameSnapshot? checkpoint = null;
        var checkpointAt = 0;
        var submitted = 0;
        // Snapshot roughly a third of the way in so both prefix + suffix are
        // non-trivial regardless of exact game length.
        const int checkpointAfter = 3;

        for (var step = 0; step < 1000; step++)
        {
            if (game.IsCompleted) break;
            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game);
            if (winner == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = NextCommand(facade, prompt) with { PlayerId = prompt.PlayerId };
            try { await facade.SubmitAsync(cmd); }
            catch (InvalidOperationException) { break; }
            submitted++;

            if (checkpoint == null && submitted >= checkpointAfter)
            {
                checkpoint = facade.SaveSnapshot();
                checkpointAt = checkpoint.Log.Count;
            }
        }

        // If the game ended before the checkpoint point, take it now.
        checkpoint ??= facade.SaveSnapshot();
        if (checkpointAt == 0) checkpointAt = checkpoint.Log.Count;

        var fullLog = facade.SaveSnapshot().Log;
        var commandsSince = fullLog.Skip(checkpointAt).ToList();
        return (facade.GetState(), fullLog, checkpoint, commandsSince);
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

    // -----------------------------------------------------------------------
    // Scenario seeding — identical across original + rehydrate.
    // -----------------------------------------------------------------------
    private static GameFacade BuildSeededFacade()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        SeedBattlefieldLand(facade.Alice, "Forest");
        SeedBattlefieldLand(facade.Alice, "Mountain");
        SeedBattlefieldLand(facade.Bob, "Island");

        var bears = SeedBattlefieldCreature(facade.Alice, "Grizzly Bears", 2, 2, legendary: false);
        bears.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);

        SeedBattlefieldCreature(facade.Alice, "Llanowar Hero", 1, 1, legendary: true);
        SeedBattlefieldCreature(facade.Alice, "Llanowar Hero", 1, 1, legendary: true);

        return facade;
    }

    private static void SeedBattlefieldLand(Player p, string name)
    {
        var land = new Land(name) { Owner = p, Controller = p };
        p.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.MarkEnteredBattlefield();
    }

    private static Creature SeedBattlefieldCreature(
        Player p, string name, int power, int toughness, bool legendary)
    {
        var supertypes = legendary ? new[] { CardSupertype.Legendary } : null;
        var c = new Creature(name, "1", power, toughness, supertypes: supertypes)
        { Owner = p, Controller = p };
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        c.MarkEnteredBattlefield();
        c.ClearSummoningSickness();
        return c;
    }

    private static object IdProjection(GameStateDto s) => new
    {
        Players = s.Players.Select(p => new
        {
            p.Name,
            p.Id,
            Hand = ZoneIds(p.Hand),
            Battlefield = p.Battlefield.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Graveyard = ZoneIds(p.Graveyard),
            Library = ZoneIds(p.Library),
            Exile = ZoneIds(p.Exile),
        }).ToList(),
        Stack = s.Stack.Select(o => $"{o.Kind}|{o.Id}").ToList(),
    };

    private static List<string> ZoneIds(ZoneDto z) =>
        z.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList();

    private static PlayerDto PlayerOf(GameFacade facade, System.Guid playerId)
        => facade.GetState().Players.First(p => p.Id == playerId);

    private static PlayerDto OpponentOf(GameFacade facade, System.Guid playerId)
        => facade.GetState().Players.First(p => p.Id != playerId);
}
