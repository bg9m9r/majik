using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Random;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Regression for the cross-game determinism bug the
/// <c>RandomLegalCommandFuzzTests</c> harness surfaced: under PARALLEL
/// execution (many concurrent id-scoped games), a card occasionally got a
/// perturbed <see cref="Card.InstanceId"/> — id-identical replay broke across
/// concurrent games even though names / zones / P-T / life all still matched
/// (the tell-tale id-only divergence).
///
/// <para><b>Actual root cause</b> (diagnosed here, not the AsyncLocal-flow
/// hypothesis the fuzz harness note recorded): the process-wide
/// <c>RouteThroughNamedFactories</c> flag was read PER CARD by
/// <c>GameFacade.Create</c>'s deck build. A concurrent test toggling that
/// global mid-build made a single deck build straddle two policies — some
/// cards routed through the named factory (which mints a different NUMBER of
/// object ids than the binder-chain shell), some not — so the build minted a
/// non-deterministic COUNT of ids from the per-game
/// <see cref="DeterministicIdSource"/>, desyncing the id sequence. The fix
/// snapshots that flag per facade so a build is internally consistent and
/// immune to a concurrent toggle; the per-facade kill-switch
/// (<c>GameFacade.Create(routeThroughNamedFactories:)</c>) replaces the
/// global mutation. <c>GameDriver</c> also now OWNS its id scope (Push, not
/// PushIfNone) as complementary ownership hygiene.</para>
///
/// <para>The property under test: each game seeds its OWN deterministic id
/// source and drives a scripted flow; replaying each from its (seed, log)
/// must yield a byte-identical <c>IdProjection</c> — and this must hold even
/// when every game runs concurrently on a shared thread pool. This test runs
/// WITHOUT the non-parallel collection pin the fuzz harness used as a
/// workaround; before the fix it failed under the assembly's cross-class
/// parallelism (a concurrent <c>FactoryRoutingTests</c> toggle perturbing a
/// build), and now passes.</para>
/// </summary>
public sealed class ConcurrentDeterministicIdTests
{
    private const int Games = 24;
    private const int MaxTurns = 4;

    [Fact]
    public async Task ConcurrentSeededGames_ReplayIdIdentically()
    {
        // Run a batch of distinct-seeded games + their replays ALL at once.
        // Each (seed) game is run twice (run + replay) under its own ambient
        // DeterministicIdScope; the two id projections must match. Running
        // every seed concurrently (and concurrently with the rest of the
        // assembly under xUnit cross-class parallelism) is the condition that
        // perturbed a per-game id sequence before the fix.
        var tasks = Enumerable.Range(1, Games)
            .Select(seed => Task.Run(() => RunSeedTwiceAsync(seed)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        foreach (var (seed, run1, run2) in results)
        {
            run2.CommandKinds.Should().Equal(run1.CommandKinds,
                $"seed {seed}: the scripted responder is a pure function of the " +
                "seed, so two runs must take the IDENTICAL decision sequence");

            IdProjection(run2.State).Should().BeEquivalentTo(
                IdProjection(run1.State), opts => opts.WithStrictOrdering(),
                $"seed {seed}: same seed + same command log must yield an " +
                "ID-IDENTICAL final state even under concurrent execution");
        }
    }

    private static async Task<(int Seed, RunResult Run1, RunResult Run2)> RunSeedTwiceAsync(int seed)
    {
        // Two runs of the SAME seed, each under its own per-game id scope, run
        // back-to-back inside this seed's task. The OTHER seeds' tasks run
        // concurrently — so each run's continuations resume on a shared pool
        // while sibling games hold DIFFERENT ambient id sources.
        var run1 = await RunOnceAsync(seed);
        var run2 = await RunOnceAsync(seed);
        return (seed, run1, run2);
    }

    private static async Task<RunResult> RunOnceAsync(int seed)
    {
        using var idScope = DeterministicIdScope.Push(new DeterministicIdSource(seed));

        var facade = BuildDeckFacade(seed);
        var rng = new GameRandom(seed);
        var kinds = new List<string>();

        await DriveAsync(facade, seed, prompt =>
        {
            var cmd = ChooseLegalCommand(facade, prompt, rng);
            kinds.Add(cmd.GetType().Name);
            return cmd;
        });

        return new RunResult(facade.GetState(), kinds);
    }

    private sealed record RunResult(GameStateDto State, IReadOnlyList<string> CommandKinds);

    private static async Task DriveAsync(
        GameFacade facade, int seed, Func<PromptDto, GameCommand> respond)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => channel.Writer.TryWrite(p));

        await facade.StartFullGameAsync(
            maxTurns: MaxTurns,
            rng: new GameRandom(seed),
            logicalClock: new LogicalClock());

        var game = facade.FullGameTask!;

        for (var step = 0; step < 600; step++)
        {
            if (game.IsCompleted) break;

            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game);
            if (winner == game) break;
            if (!await read) break;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = respond(prompt) with { PlayerId = prompt.PlayerId };
            try
            {
                await facade.SubmitAsync(cmd);
            }
            catch (InvalidOperationException)
            {
                break;
            }
        }
    }

    // Conservative scripted responder — fully derived from the prompt + public
    // state so both runs of a seed take the identical path.
    private static GameCommand ChooseLegalCommand(
        GameFacade facade, PromptDto prompt, GameRandom rng)
    {
        var kinds = prompt.ExpectedKinds;

        if (kinds.Contains(nameof(MulliganCommand)))
            return new MulliganCommand(Keep: rng.Next(2) == 0);

        if (kinds.Contains(nameof(ChooseCardsToBottomCommand)))
        {
            var n = prompt.BottomCount ?? 0;
            var hand = PlayerOf(facade, prompt.PlayerId).Hand.Cards;
            var ids = hand.Take(n).Select(c => c.InstanceId).ToList();
            return new ChooseCardsToBottomCommand(ids);
        }

        if (kinds.Contains(nameof(ChooseManaCommand)))
            return new ChooseManaCommand(Array.Empty<Guid>());

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
            return new DeclareBlockersCommand(Array.Empty<BlockerDeclarationDto>());

        if (kinds.Contains(nameof(OrderTriggersCommand)))
            return new OrderTriggersCommand(Array.Empty<Guid>());

        // Priority window: play a land if we can, else cast a spell, else pass.
        var meP = PlayerOf(facade, prompt.PlayerId);
        if (kinds.Contains(nameof(PlayLandCommand)))
        {
            var land = meP.Hand.Cards.FirstOrDefault(c => c.Types.Contains("Land"));
            if (land != null && rng.Next(2) == 0)
                return new PlayLandCommand(land.InstanceId);
        }
        if (kinds.Contains(nameof(CastSpellCommand)))
        {
            var spell = meP.Hand.Cards.FirstOrDefault(c => !c.Types.Contains("Land"));
            if (spell != null && rng.Next(3) == 0)
                return new CastSpellCommand(spell.InstanceId, Array.Empty<Guid>(), null, null);
        }

        return new PassPriorityCommand();
    }

    private static GameFacade BuildDeckFacade(int seed)
    {
        var repo = new EmbeddedCardRepository();
        var aliceDeck = BuildDeck(seed);
        var bobDeck = BuildDeck(seed + 7919);
        // Pin the per-game routing policy so this game's card-build mint count
        // is independent of any concurrent toggle of the process-wide flag.
        return GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck,
            cardRepo: repo, routeThroughNamedFactories: true);
    }

    private static IReadOnlyList<ICard> BuildDeck(int seed)
    {
        var deck = new List<ICard>();
        for (var i = 0; i < 8; i++) deck.Add(new Land("Forest"));
        for (var i = 0; i < 4; i++) deck.Add(new Land("Mountain"));
        for (var i = 0; i < 6; i++) deck.Add(new Creature("Grizzly Bears", "1G", 2, 2));
        for (var i = 0; i < 4; i++) deck.Add(new Creature("Llanowar Elves", "G", 1, 1));
        for (var i = 0; i < 6; i++) deck.Add(new Instant("Lightning Bolt", "R"));
        return Shuffle(deck, new GameRandom(seed));
    }

    private static List<T> Shuffle<T>(List<T> items, GameRandom rng)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }

    private static object IdProjection(GameStateDto s) => new
    {
        Players = s.Players.Select(p => new
        {
            p.Name,
            p.Id,
            p.Life,
            p.HasLost,
            Hand = ZoneIds(p.Hand),
            Battlefield = p.Battlefield.Cards
                .Select(c => $"{c.Name}|{c.InstanceId}|{c.Power}/{c.Toughness}|t={c.Tapped}").ToList(),
            Graveyard = ZoneIds(p.Graveyard),
            Library = ZoneIds(p.Library),
            Exile = ZoneIds(p.Exile),
        }).ToList(),
        Stack = s.Stack.Select(o => $"{o.Kind}|{o.Id}").ToList(),
        s.TurnNumber,
        s.Phase,
        s.ActivePlayerId,
    };

    private static List<string> ZoneIds(ZoneDto z) =>
        z.Cards.Select(c => $"{c.Name}|{c.InstanceId}").ToList();

    private static PlayerDto PlayerOf(GameFacade facade, Guid playerId)
        => facade.GetState().Players.First(p => p.Id == playerId);

    private static PlayerDto OpponentOf(GameFacade facade, Guid playerId)
        => facade.GetState().Players.First(p => p.Id != playerId);
}
