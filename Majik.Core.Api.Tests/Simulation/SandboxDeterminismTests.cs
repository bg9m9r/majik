using System.Text.Json;
using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests.Simulation;

/// <summary>
/// Proves R5: same seed + same cloned state ⇒ identical outcome.
/// Each Run() clones from the SAME original alice/bob (built once, outside
/// Run). Cloning must not mutate the originals (Tasks 1–11 proved it
/// doesn't), so two independent runs start from identical state.
/// </summary>
public sealed class SandboxDeterminismTests
{
    [Fact]
    public async Task Sandbox_SameSeed_SameOutcome()
    {
        // Build the board once, outside Run(), so both runs clone from the same
        // original objects. Cloning is non-mutating, so run-1 leaves the
        // originals byte-identical for run-2.
        var (alice, bob) = BuildBoard();

        async Task<string> Run(int seed)
        {
            var sandbox = SandboxGame.From(
                new[] { alice, bob },
                rng: new GameRandom(seed),
                agentFactory: _ => new DeterministicBotAgent());

            await sandbox.Driver.RunGameAsync(
                maxTurns: 5,
                startingPlayerIndex: 0,
                CancellationToken.None);

            var s = sandbox.State;
            // Need a Stack for StateSnapshotter (it may be empty post-sim since the
            // sandbox used its own internal stack). Supply a fresh empty stack so
            // the snapshot call is valid. The interesting determinism surface is the
            // player/zone state, not the stack contents (which are always empty
            // mid-cleanup after the bot passes all priority).
            var emptyStack = new Majik.Core.Stack.Stack();

            var dto = StateSnapshotter.Snapshot(
                gameId: Guid.Empty,
                turnNumber: 5,
                phase: StepStateType.PreCombatMain,
                activePlayer: s.Players[0],
                players: s.Players,
                stack: emptyStack);

            return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
        }

        var run1 = await Run(12345);
        var run2 = await Run(12345);

        run1.Should().Be(run2,
            "same seed + same initial state must produce byte-identical outcomes (R5: determinism)");
    }

    /// <summary>
    /// Builds a small board: two players each with a few creatures and lands,
    /// plus a small library so the game can run 5 turns without hitting an
    /// empty library. All cards are set up on the battlefield or in the library
    /// outside Run() so both clone runs start from identical state.
    /// </summary>
    private static (Player alice, Player bob) BuildBoard()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        // Put lands on the battlefield so the bot can untap them.
        for (var i = 0; i < 3; i++)
        {
            var forest = new Land("Forest");
            forest.ChangeOwner(alice);
            forest.ChangeController(alice);
            alice.Zones.Battlefield.AddCard(forest);
        }

        for (var i = 0; i < 3; i++)
        {
            var mountain = new Land("Mountain");
            mountain.ChangeOwner(bob);
            mountain.ChangeController(bob);
            bob.Zones.Battlefield.AddCard(mountain);
        }

        // Small libraries so there is no empty-library loss during 5 turns.
        for (var i = 0; i < 15; i++)
        {
            var card = new Land("Forest");
            card.ChangeOwner(alice);
            alice.Zones.Library.AddCard(card);
        }

        for (var i = 0; i < 15; i++)
        {
            var card = new Land("Mountain");
            card.ChangeOwner(bob);
            bob.Zones.Library.AddCard(card);
        }

        return (alice, bob);
    }
}
