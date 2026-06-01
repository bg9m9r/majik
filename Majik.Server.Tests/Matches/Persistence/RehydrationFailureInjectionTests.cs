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
/// PLAN 08 (body) — failure injection. Models a replica handoff: replica A
/// starts + drives a match (recording every command to a SHARED durable store),
/// then A's in-process facade is dropped (crash). Replica B — a fresh coordinator
/// + fresh GameRegistry sharing the same durable store — rehydrates the game from
/// the store and CONTINUES it, with the command seq stream remaining contiguous
/// across the handoff. Also: a double-claim race rehydrates exactly one facade.
/// </summary>
public class RehydrationFailureInjectionTests
{
    private const int Seed = 90909;

    [Fact]
    public async Task ReplicaB_RehydratesDroppedGame_AndContinuesWithContiguousSeqStream()
    {
        var matchId = Guid.NewGuid();
        // SHARED durable store — the thing that survives replica A's crash.
        var log = new InMemoryEngineCommandLogStore();
        var checkpoints = new InMemoryEngineCheckpointStore();

        // ── Replica A: a GameRegistry + a coordinator over the shared store ──
        var registryA = new GameRegistry();
        var coordA = Build(log, checkpoints);

        GameStateDto stateAfterA;
        long lastSeqOnA;
        using var crashCts = new CancellationTokenSource();
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var facadeA = BuildSeededFacade();
            registryA.RegisterRehydrated(matchId, facadeA); // stand-in for create+register
            (stateAfterA, lastSeqOnA) = await DriveAndRecordAsync(
                facadeA, matchId, coordA, stopAfter: 4, gameCt: crashCts.Token);
        }
        lastSeqOnA.Should().BeGreaterThan(0, "replica A must have logged some commands");

        // ── Crash A: cancel its background game loop + drop its facade so a
        //    lookup on A misses (models the process dying with the game in-flight).
        crashCts.Cancel();
        registryA.Remove(matchId);
        registryA.Get(matchId).Should().BeNull("replica A's facade is gone (crash)");

        // ── Replica B: fresh registry + coordinator sharing the durable store ─
        var registryB = new GameRegistry();
        var coordB = Build(log, checkpoints);
        registryB.Get(matchId).Should().BeNull("replica B never had this game in-process");

        GameFacade? facadeB;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            facadeB = await coordB.TryRehydrateAsync(
                matchId, Seed, BuildSeededFacade, CancellationToken.None);
        }
        facadeB.Should().NotBeNull("replica B must rehydrate the dropped game from the durable store");

        // Serve under the original match id + register on B — the wiring
        // MatchService.TryRehydrateAndDispatchAsync performs.
        facadeB!.OverrideGameId(matchId);
        registryB.RegisterRehydrated(matchId, facadeB).Should().BeTrue();

        // Rehydrated state matches the crashed replica's state id-identically.
        IdProjection(facadeB.GetState()).Should().BeEquivalentTo(
            IdProjection(stateAfterA), opts => opts.WithStrictOrdering(),
            "replica B's rehydrated state must match where replica A left off");

        // ── Contiguity across the A→B handoff ───────────────────────────────
        // The durable log A produced is a contiguous 1..lastSeq stream, and B
        // consumed exactly that stream to reach the same point (its action-log
        // count equals A's last seq) — so when B records its NEXT command it does
        // so at lastSeq+1, continuing the seq stream without a gap.
        var all = await log.ReadSinceAsync(matchId, -1, CancellationToken.None);
        all.Should().HaveCount((int)lastSeqOnA,
            "replica A's durable log must be a contiguous 1..lastSeq stream (no gaps)");
        facadeB.Log.Actions.Count.Should().Be((int)lastSeqOnA,
            "replica B must have replayed exactly the contiguous durable stream, so its " +
            "next recorded command lands at lastSeq+1 — a seamless continuation");

        // And B can durably record a further command at the contiguous next seq.
        var nextSeq = facadeB.Log.Actions.Count + 1;
        await coordB.RecordCommandAsync(matchId, facadeB, nextSeq, new PassPriorityCommand(), default);
        (await log.MaxSeqAsync(matchId, default)).Should().Be(nextSeq,
            "B's continuation appends contiguously past A's last seq");
    }

    [Fact]
    public async Task DoubleClaim_RehydratesExactlyOneFacade()
    {
        var matchId = Guid.NewGuid();
        var log = new InMemoryEngineCommandLogStore();
        var checkpoints = new InMemoryEngineCheckpointStore();
        var coord = Build(log, checkpoints);

        // Seed the durable store with a driven game.
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var facade = BuildSeededFacade();
            await DriveAndRecordAsync(facade, matchId, coord, stopAfter: 4);
        }

        // Two replicas both try to rehydrate + register into the SAME registry
        // (the registry's TryAdd is the SETNX analogue — exactly one wins).
        var registry = new GameRegistry();

        async Task<bool> ClaimAndRegister()
        {
            GameFacade f;
            using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
            {
                f = (await coord.TryRehydrateAsync(matchId, Seed, BuildSeededFacade, default))!;
            }
            f.OverrideGameId(matchId);
            var won = registry.RegisterRehydrated(matchId, f);
            if (!won) f.Dispose();
            return won;
        }

        var results = await Task.WhenAll(ClaimAndRegister(), ClaimAndRegister());

        results.Count(won => won).Should().Be(1,
            "a double-claim must register EXACTLY ONE rehydrated facade — the loser backs off");
        registry.Count.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static EnginePersistenceCoordinator Build(
        IEngineCommandLogStore log, IEngineCheckpointStore checkpoints) =>
        new(log, checkpoints,
            Options.Create(new EnginePersistenceOptions { Enabled = true, CheckpointEveryCommands = 3 }));

    /// <summary>Drive at most <paramref name="stopAfter"/> commands, recording
    /// each at its facade seq through the coordinator. Returns the state + the
    /// last recorded seq.</summary>
    private static async Task<(GameStateDto State, long LastSeq)> DriveAndRecordAsync(
        GameFacade facade, Guid matchId, EnginePersistenceCoordinator coord, int stopAfter,
        CancellationToken gameCt = default)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => channel.Writer.TryWrite(p));
        await facade.StartFullGameAsync(
            maxTurns: 3, rng: new GameRandom(Seed), logicalClock: new LogicalClock(), ct: gameCt);
        var game = facade.FullGameTask!;

        long lastSeq = 0;
        var done = 0;
        for (var step = 0; step < 1000 && done < stopAfter; step++)
        {
            if (game.IsCompleted) break;
            var read = channel.Reader.WaitToReadAsync().AsTask();
            if (await Task.WhenAny(read, game) == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = NextCommand(facade, prompt) with { PlayerId = prompt.PlayerId };
            try { await facade.SubmitAsync(cmd); } catch (InvalidOperationException) { break; }
            lastSeq = facade.Log.Actions.Count;
            await coord.RecordCommandAsync(matchId, facade, lastSeq, cmd, default);
            done++;
        }
        return (facade.GetState(), lastSeq);
    }

    private static GameCommand NextCommand(GameFacade facade, PromptDto prompt)
    {
        var kinds = prompt.ExpectedKinds;
        if (kinds.Contains(nameof(MulliganCommand))) return new MulliganCommand(Keep: true);
        if (kinds.Contains(nameof(ChooseCardsToBottomCommand)))
        {
            var n = prompt.BottomCount ?? 0;
            var hand = facade.GetState().Players.First(p => p.Id == prompt.PlayerId).Hand.Cards;
            return new ChooseCardsToBottomCommand(hand.Take(n).Select(c => c.InstanceId).ToList());
        }
        if (kinds.Contains(nameof(DeclareAttackersCommand)))
        {
            var me = facade.GetState().Players.First(p => p.Id == prompt.PlayerId);
            var opp = facade.GetState().Players.First(p => p.Id != prompt.PlayerId);
            var attackers = me.Battlefield.Cards
                .Where(c => c.Types.Contains("Creature") && !c.Tapped && !c.SummoningSickness)
                .Select(c => new AttackerDeclarationDto(c.InstanceId, opp.Id)).ToList();
            return new DeclareAttackersCommand(attackers);
        }
        if (kinds.Contains(nameof(DeclareBlockersCommand)))
            return new DeclareBlockersCommand(System.Array.Empty<BlockerDeclarationDto>());
        if (kinds.Contains(nameof(OrderTriggersCommand)))
            return new OrderTriggersCommand(System.Array.Empty<System.Guid>());
        return new PassPriorityCommand();
    }

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
            p.Name, p.Id,
            Battlefield = p.Battlefield.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Hand = p.Hand.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Graveyard = p.Graveyard.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Library = p.Library.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
        }).ToList(),
        Stack = s.Stack.Select(o => $"{o.Kind}|{o.Id}").ToList(),
    };
}
