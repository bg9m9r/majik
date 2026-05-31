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
/// PLAN 08 prerequisite — the executable definition of done for the
/// determinism fix: build a facade with an explicit <see cref="GameRandom"/>
/// seed + a per-game <see cref="LogicalClock"/>, drive a scripted command log
/// through <see cref="GameFacade.SubmitAsync"/> across a turn (combat-relevant
/// permanents, a legend-rule conflict, a layer effect), then build a SECOND
/// facade with the SAME seed + SAME commands and assert the resulting game
/// state is STRUCTURALLY equivalent.
///
/// <para>"Structurally" deliberately ignores the still-nondeterministic
/// portal-facing ids (<c>Card.InstanceId</c>, <c>Player.Id</c>, ability/stack
/// ids) — the deferred id-reseeding work. We compare zones by card name +
/// order, life totals, computed P/T, tapped state, phase, turn number, and
/// stack contents. This proves the engine's game DECISIONS are reproducible
/// given (seed, commands).</para>
/// </summary>
public class DeterminismReplayTests
{
    [Fact]
    public async Task SameSeedSameCommands_YieldStructurallyIdenticalState()
    {
        const int seed = 42;

        // Both runs use the SAME deterministic decision policy + the SAME RNG
        // seed + a fresh per-game logical clock, and drive the game through the
        // real SubmitAsync path. The policy resolves each command against the
        // run's OWN live state (so the still-nondeterministic instance ids are
        // resolved per-run — verbatim id-keyed command replay is the deferred
        // id-reseeding step, NOT this slice). The command-KIND sequence and the
        // final game state must be structurally identical.
        var run1 = await RunAsync(seed);
        var run2 = await RunAsync(seed);

        run1.CommandKinds.Should().NotBeEmpty(
            "the scenario must actually submit commands through SubmitAsync");

        // The decision sequence itself is reproducible (commands modulo ids).
        run2.CommandKinds.Should().Equal(run1.CommandKinds);

        // And the resulting game state is structurally identical (ids ignored).
        Structural(run2.State).Should().BeEquivalentTo(
            Structural(run1.State), opts => opts.WithStrictOrdering());
    }

    private sealed record RunResult(GameStateDto State, IReadOnlyList<string> CommandKinds);

    private static async Task<RunResult> RunAsync(int seed)
    {
        var facade = BuildSeededFacade();
        var kinds = new List<string>();
        await DriveAsync(facade, seed, prompt =>
        {
            var cmd = NextCommand(facade, prompt);
            // Record the command KIND (not its nondeterministic ids) so the two
            // runs' decision sequences can be compared structurally.
            kinds.Add(cmd.GetType().Name);
            return cmd;
        });
        return new RunResult(facade.GetState(), kinds);
    }

    /// <summary>
    /// Drive a full game with the given seed + a fresh logical clock,
    /// responding to every prompt via <paramref name="respond"/> and submitting
    /// through the real <see cref="GameFacade.SubmitAsync"/> path.
    ///
    /// <para>The prompt observer is attached BEFORE StartFullGameAsync so the
    /// very first prompt (which fires synchronously during start) is captured —
    /// prompts are pushed into an unbounded channel and the pump drains them.
    /// Each submitted command causes the engine to raise the next prompt into
    /// the channel, until the game completes.</para>
    /// </summary>
    private static async Task DriveAsync(
        GameFacade facade, int seed, Func<PromptDto, GameCommand> respond)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PromptDto>();
        using var sub = facade.SubscribePrompts(p => channel.Writer.TryWrite(p));

        await facade.StartFullGameAsync(
            maxTurns: 2,
            rng: new GameRandom(seed),
            logicalClock: new LogicalClock());

        var game = facade.FullGameTask!;

        // Bounded by step count so a logic bug can never hang the suite.
        for (var step = 0; step < 1000; step++)
        {
            if (game.IsCompleted) return;

            // Wait for either the next prompt or game completion.
            var read = channel.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(read, game);
            if (winner == game) return;
            if (!await read) return;
            if (!channel.Reader.TryRead(out var prompt)) continue;

            var cmd = respond(prompt) with { PlayerId = prompt.PlayerId };
            try
            {
                await facade.SubmitAsync(cmd);
            }
            catch (InvalidOperationException)
            {
                // Bot/closed seat or already-finished game — stop driving.
                return;
            }
        }
    }

    /// <summary>
    /// Deterministic responder: keep on mulligan, bottom the first N cards,
    /// attack with all eligible creatures, never block, otherwise pass.
    /// Deliberately conservative + fully derived from the prompt + public
    /// state so both runs take the identical path.
    /// </summary>
    private static GameCommand NextCommand(GameFacade facade, PromptDto prompt)
    {
        var kinds = prompt.ExpectedKinds;

        if (kinds.Contains(nameof(MulliganCommand)))
            return new MulliganCommand(Keep: true);

        if (kinds.Contains(nameof(ChooseCardsToBottomCommand)))
        {
            var n = prompt.BottomCount ?? 0;
            var hand = HandOf(facade, prompt.PlayerId);
            var ids = hand.Take(n).Select(c => c.InstanceId).ToList();
            return new ChooseCardsToBottomCommand(ids);
        }

        if (kinds.Contains(nameof(DeclareAttackersCommand)))
        {
            // Attack with every untapped, non-summoning-sick creature this
            // player controls, swinging at the opponent.
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
            // Engine default order (empty = "as presented").
            return new OrderTriggersCommand(System.Array.Empty<System.Guid>());

        // Default: pass priority (covers PassPriorityCommand prompts and any
        // optional choice we don't script).
        return new PassPriorityCommand();
    }

    // -----------------------------------------------------------------------
    // Scenario seeding — identical across both runs.
    // -----------------------------------------------------------------------
    private static GameFacade BuildSeededFacade()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        // Lands so each player has board presence; a same-name legendary pair
        // under Alice (legend-rule conflict at game start, resolved by the
        // logical-clock ETB order); an attacker so a combat step has work.
        SeedBattlefieldLand(facade.Alice, "Forest");
        SeedBattlefieldLand(facade.Alice, "Mountain");
        SeedBattlefieldLand(facade.Bob, "Island");

        SeedBattlefieldCreature(facade.Alice, "Grizzly Bears", 2, 2, legendary: false);
        SeedBattlefieldCreature(facade.Alice, "Llanowar Hero", 1, 1, legendary: true);
        SeedBattlefieldCreature(facade.Alice, "Llanowar Hero", 1, 1, legendary: true);

        return facade;
    }

    private static void SeedBattlefieldLand(Player p, string name)
    {
        var land = new Land(name);
        land.Owner = p;
        land.Controller = p;
        p.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.MarkEnteredBattlefield();
    }

    private static void SeedBattlefieldCreature(
        Player p, string name, int power, int toughness, bool legendary)
    {
        var supertypes = legendary ? new[] { CardSupertype.Legendary } : null;
        var c = new Creature(name, "1", power, toughness, supertypes: supertypes)
        { Owner = p, Controller = p };
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        // Logical-clock ETB timestamp — the legend SBA uses this to decide
        // which copy survives (deterministically, identically each run).
        c.MarkEnteredBattlefield();
        c.ClearSummoningSickness(); // ready to attack
    }

    // -----------------------------------------------------------------------
    // Structural snapshot — ids stripped.
    // -----------------------------------------------------------------------
    private static object Structural(GameStateDto s) => new
    {
        s.TurnNumber,
        s.Phase,
        ActivePlayerName = NameOf(s, s.ActivePlayerId),
        Players = s.Players.Select(p => new
        {
            p.Name,
            p.Life,
            p.HasLost,
            Hand = ZoneNames(p.Hand),
            Battlefield = p.Battlefield.Cards
                .Select(c => $"{c.Name}|{c.Power}/{c.Toughness}|tapped={c.Tapped}")
                .ToList(),
            Graveyard = ZoneNames(p.Graveyard),
            Library = ZoneNames(p.Library),
            Exile = ZoneNames(p.Exile),
        }).ToList(),
        Stack = s.Stack.Select(o => $"{o.Kind}|{o.Description}").ToList(),
    };

    private static List<string> ZoneNames(ZoneDto z) =>
        z.Cards.Select(c => c.Name).ToList();

    private static string NameOf(GameStateDto s, System.Guid id) =>
        s.Players.FirstOrDefault(p => p.Id == id)?.Name ?? "?";

    private static IReadOnlyList<CardSnapshotDto> HandOf(GameFacade facade, System.Guid playerId)
        => PlayerOf(facade, playerId).Hand.Cards;

    private static PlayerDto PlayerOf(GameFacade facade, System.Guid playerId)
        => facade.GetState().Players.First(p => p.Id == playerId);

    private static PlayerDto OpponentOf(GameFacade facade, System.Guid playerId)
        => facade.GetState().Players.First(p => p.Id != playerId);
}
