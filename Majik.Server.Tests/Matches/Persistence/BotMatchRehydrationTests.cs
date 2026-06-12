using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.BotReplay;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Random;
using Majik.Server.Composition;
using Majik.Server.Matches.Persistence;
using Microsoft.Extensions.Options;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Server.Tests.Matches.Persistence;

/// <summary>
/// Deferral #14's missing end-to-end test — a vs-bot match whose Bob seat runs
/// the WALL-CLOCK-NONDETERMINISTIC <c>mcts</c> strategy survives a replica
/// crash: every bot answer was durably recorded at the IPlayerAgent boundary,
/// so the rehydrating replica replays the answers VERBATIM (ScriptedPlayerAgent;
/// nothing recomputes) and reaches the live edge with IDENTICAL state — life
/// totals, battlefield InstanceIds, command seq — regardless of wall-clock
/// variance. A paired control with a deliberately perturbed record proves the
/// desync guard fails GRACEFULLY (no wedge, no crash).
/// </summary>
public class BotMatchRehydrationTests
{
    private const int Seed = 314159;
    private const string Archetype = "gruul";

    [Fact]
    public async Task MctsBotMatch_Rehydrates_IdenticallyToTheCrashedOriginal()
    {
        var matchId = Guid.NewGuid();

        // SHARED durable stores — what survives replica A's crash.
        var commandLog = new InMemoryEngineCommandLogStore();
        var checkpoints = new InMemoryEngineCheckpointStore();
        var botDecisions = new InMemoryBotDecisionLogStore();

        // ── Replica A: live mcts bot match, recording commands + decisions ──
        var coordA = BuildCoordinator(commandLog, checkpoints, botDecisions);
        var factoryA = BuildMctsFactory();

        GameStateDto stateAfterA;
        long lastSeqOnA;
        using var crashCts = new CancellationTokenSource();
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var facadeA = factoryA.Create(
                "Alice", "Bot", BuildDeck(), BuildDeck(),
                botSeatArchetype: Archetype,
                botDecisionRecorder: r => coordA.RecordBotDecisionAsync(matchId, r, default));

            (stateAfterA, lastSeqOnA) = await DriveAndRecordAsync(
                facadeA, matchId, coordA, stopAfter: 10, gameCt: crashCts.Token);
        }
        lastSeqOnA.Should().BeGreaterThan(0, "replica A must have logged some human commands");

        var recordedStream = await botDecisions.ReadAllAsync(matchId, default);
        recordedStream.Should().NotBeEmpty(
            "the mcts bot must have made (and recorded) in-engine decisions");
        recordedStream.Select(r => r.BotSeq).Should().BeInAscendingOrder()
            .And.OnlyHaveUniqueItems("the bot-decision stream is contiguous and monotonic");

        // ── Crash replica A ─────────────────────────────────────────────────
        crashCts.Cancel();
        factoryA.Delete(stateAfterA.GameId).Should().BeTrue("replica A's facade is dropped (crash)");

        // ── Replica B: rehydrate from the shared stores ─────────────────────
        var coordB = BuildCoordinator(commandLog, checkpoints, botDecisions);
        var factoryB = BuildMctsFactory();
        var script = await coordB.ReadBotDecisionsAsync(matchId, default);
        script.Should().HaveSameCount(recordedStream);

        GameFacade? facadeB;
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            facadeB = await coordB.TryRehydrateAsync(
                matchId, Seed,
                buildFreshFacade: () => factoryB.BuildUnregisteredFacade(
                    "Alice", "Bot", BuildDeck(), BuildDeck(),
                    botSeatArchetype: Archetype,
                    botReplayScript: script,
                    botDecisionRecorder: r => coordB.RecordBotDecisionAsync(matchId, r, default)),
                CancellationToken.None);
        }

        facadeB.Should().NotBeNull("replica B must rehydrate the crashed mcts bot match");

        // The rehydrated state is IDENTICAL to where the original crashed —
        // works regardless of wall-clock variance because NOTHING recomputed:
        // the bot's answers were replayed verbatim from the recorded stream.
        Projection(facadeB!.GetState()).Should().BeEquivalentTo(
            Projection(stateAfterA), opts => opts.WithStrictOrdering(),
            "an mcts bot match must rehydrate id-identically via its recorded decisions");

        // Replay consumed exactly the contiguous human-command stream — the
        // next recorded command lands at lastSeq+1 (seamless continuation).
        facadeB.Log.Actions.Count.Should().Be((int)lastSeqOnA);
    }

    [Fact]
    public async Task PerturbedBotDecision_FailsRehydrationGracefully_NoWedgeNoCrash()
    {
        var matchId = Guid.NewGuid();
        var commandLog = new InMemoryEngineCommandLogStore();
        var checkpoints = new InMemoryEngineCheckpointStore();
        var botDecisions = new InMemoryBotDecisionLogStore();
        var coordA = BuildCoordinator(commandLog, checkpoints, botDecisions);
        var factoryA = BuildMctsFactory();

        GameStateDto stateAfterA;
        using var crashCts = new CancellationTokenSource();
        using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
        {
            var facadeA = factoryA.Create(
                "Alice", "Bot", BuildDeck(), BuildDeck(),
                botSeatArchetype: Archetype,
                botDecisionRecorder: r => coordA.RecordBotDecisionAsync(matchId, r, default));
            (stateAfterA, _) = await DriveAndRecordAsync(
                facadeA, matchId, coordA, stopAfter: 10, gameCt: crashCts.Token);
        }
        crashCts.Cancel();
        factoryA.Delete(stateAfterA.GameId);

        // Perturb one recorded payload: swap a Guid for a fresh one (an id
        // that resolves against nothing on the rebuilt facade).
        var original = await botDecisions.ReadAllAsync(matchId, default);
        var perturbed = Perturb(original);

        var coordB = BuildCoordinator(commandLog, checkpoints, botDecisions);
        var factoryB = BuildMctsFactory();

        GameFacade? facadeB = null;
        var act = async () =>
        {
            using (DeterministicIdScope.Push(new DeterministicIdSource(Seed)))
            {
                facadeB = await coordB.TryRehydrateAsync(
                    matchId, Seed,
                    buildFreshFacade: () => factoryB.BuildUnregisteredFacade(
                        "Alice", "Bot", BuildDeck(), BuildDeck(),
                        botSeatArchetype: Archetype,
                        botReplayScript: perturbed,
                        botDecisionRecorder: r => Task.CompletedTask),
                    CancellationToken.None);
            }
        };

        // The desync guard throws INSIDE the replay (ScriptedPlayerAgent /
        // codec decode), which lands in the existing graceful replay stop —
        // the rehydrate call itself must complete without crashing or wedging.
        await act.Should().NotThrowAsync(
            "a perturbed bot-decision stream must stop the replay gracefully, not crash");

        // And the guard FIRED: whatever came back (null or a partially
        // replayed facade) must NOT silently equal the original live state.
        if (facadeB != null)
        {
            Projection(facadeB.GetState()).Should().NotBeEquivalentTo(
                Projection(stateAfterA),
                "the perturbed stream must not silently reproduce the original state — " +
                "the desync guard must have stopped the replay early");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static EnginePersistenceCoordinator BuildCoordinator(
        IEngineCommandLogStore log,
        IEngineCheckpointStore checkpoints,
        IBotDecisionLogStore botDecisions) =>
        new(log, checkpoints,
            Options.Create(new EnginePersistenceOptions
            {
                Enabled = true,
                // Checkpoint mid-game so the command prefix comes from a
                // checkpoint while the bot-decision stream still replays
                // WHOLE from botSeq 0 (no checkpoint interplay).
                CheckpointEveryCommands = 4,
            }),
            botDecisions: botDecisions);

    /// <summary>
    /// Server factory configured for the WALL-CLOCK-BUDGETED mcts strategy
    /// (tiny budget — the nondeterminism source stays intact; iteration counts
    /// vary run-to-run with load, which is exactly what recorded decisions
    /// must absorb).
    /// </summary>
    private static ServerGameFactory BuildMctsFactory() =>
        new(new GameRegistry(), botOptions: new ServerBotOptions
        {
            Strategy = "mcts",
            MaxMctsIterations = 40,
            MaxMctsBudgetMs = 120,
        });

    /// <summary>Drive the HUMAN (Alice) seat for at most
    /// <paramref name="stopAfter"/> commands, durably recording each at its
    /// facade seq. The bot (Bob) seat drives itself in-engine — its answers
    /// flow through the RecordingPlayerAgent installed by the factory.</summary>
    private static async Task<(GameStateDto State, long LastSeq)> DriveAndRecordAsync(
        GameFacade facade, Guid matchId, EnginePersistenceCoordinator coord, int stopAfter,
        CancellationToken gameCt)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => channel.Writer.TryWrite(p));
        await facade.StartFullGameAsync(
            maxTurns: 6, rng: new GameRandom(Seed), logicalClock: new LogicalClock(), ct: gameCt);
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
            return new DeclareAttackersCommand(Array.Empty<AttackerDeclarationDto>());
        if (kinds.Contains(nameof(DeclareBlockersCommand)))
            return new DeclareBlockersCommand(Array.Empty<BlockerDeclarationDto>());
        if (kinds.Contains(nameof(OrderTriggersCommand)))
            return new OrderTriggersCommand(Array.Empty<Guid>());
        return new PassPriorityCommand();
    }

    private static IReadOnlyList<ICard> BuildDeck()
    {
        var cards = new List<ICard>();
        for (var i = 0; i < 24; i++) cards.Add(new Land("Forest"));
        for (var i = 0; i < 12; i++) cards.Add(new Creature("Grizzly Bears", "1G", 2, 2));
        return cards;
    }

    /// <summary>Swap one Guid inside the first id-bearing recorded payload for
    /// a fresh Guid that resolves against nothing on the rebuilt facade.</summary>
    private static IReadOnlyList<BotDecisionRecord> Perturb(IReadOnlyList<BotDecisionRecord> records)
    {
        var list = records.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            BotDecisionPayload? swapped = list[i].Payload switch
            {
                PlayLandPayload p => p with { LandId = Guid.NewGuid() },
                CastSpellPayload p => p with { CardId = Guid.NewGuid() },
                CardsToBottomPayload { CardIds.Count: > 0 } p => p with
                {
                    CardIds = p.CardIds.Skip(1).Prepend(Guid.NewGuid()).ToList(),
                },
                AttackersPayload { Attackers.Count: > 0 } p => p with
                {
                    Attackers = p.Attackers.Skip(1)
                        .Prepend(p.Attackers[0] with { AttackerId = Guid.NewGuid() }).ToList(),
                },
                BlockersPayload { Pairs.Count: > 0 } p => p with
                {
                    Pairs = p.Pairs.Skip(1)
                        .Prepend(p.Pairs[0] with { BlockerId = Guid.NewGuid() }).ToList(),
                },
                ManaSourcesPayload { SourceIds.Count: > 0 } p => p with
                {
                    SourceIds = p.SourceIds.Skip(1).Prepend(Guid.NewGuid()).ToList(),
                },
                _ => null,
            };
            if (swapped != null)
            {
                list[i] = list[i] with { Payload = swapped };
                return list;
            }
        }

        // No id-bearing payload recorded (all passes/scalars) — flip a kind
        // instead; the kind-mismatch guard is the same graceful stop.
        list[0] = list[0] with
        {
            Kind = list[0].Kind == BotDecisionKind.X ? BotDecisionKind.YesNo : BotDecisionKind.X,
        };
        return list;
    }

    private static object Projection(GameStateDto s) => new
    {
        Players = s.Players.Select(p => new
        {
            p.Name,
            p.Id,
            p.Life,
            Battlefield = p.Battlefield.Cards.Select(c => $"{c.Name}|{c.InstanceId}|T:{c.Tapped}").ToList(),
            Hand = p.Hand.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Graveyard = p.Graveyard.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
            Library = p.Library.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList(),
        }).ToList(),
        Stack = s.Stack.Select(o => $"{o.Kind}|{o.Id}").ToList(),
    };
}
