using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.StateMachine;
using Majik.Core.Stack;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

public class SearchAgentTests
{
    /// <summary>
    /// Unit-test the TCS mechanism directly without the engine.
    /// Simulates the engine calling DeclareAttackersAsync from a background Task.
    /// </summary>
    [Fact]
    public async Task SearchAgent_TcsMechanism_ProducesCorrectDecisionAndResumes()
    {
        var player = new Player("Alice", 20);
        var agent = new SearchAgent(player);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.ChangeOwner(player);
        player.Zones.Battlefield.AddCard(bear);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(
            self: player,
            allPlayers: new[] { player },
            activePlayer: player,
            turnNumber: 1,
            currentPhase: StepStateType.DeclareAttackers,
            stack: stack);

        // Simulate engine calling DeclareAttackersAsync on a background thread.
        var eligible = new[] { bear };
        var engineTask = Task.Run(async () =>
            await agent.DeclareAttackersAsync(ctx, eligible));

        // Search side: await the decision.
        var decision = await agent.NextDecisionAsync().WaitAsync(TimeSpan.FromSeconds(3));

        decision.Kind.Should().Be(SimDecisionKind.DeclareAttackers);
        decision.LegalMoves.Should().NotBeEmpty();
        decision.LegalMoves.Should().Contain(m => m.IsEmptyAttack);
        decision.LegalMoves.Should().Contain(m => m.IsAllOutAttack);

        // Supply the empty-attack move.
        agent.SupplyMove(decision.LegalMoves.First(m => m.IsEmptyAttack));

        // Engine should now return CombatPlan.None.
        var plan = await engineTask.WaitAsync(TimeSpan.FromSeconds(3));
        plan.Attackers.Should().BeEmpty("we chose the empty-attack move");
    }

    /// <summary>
    /// Full end-to-end test: SearchAgent pauses at DeclareAttackers in a
    /// real sandbox engine run. Verifies legal moves and no deadlock.
    ///
    /// Note: when resuming at PhaseStateType.Combat the engine first runs a
    /// BeginningOfCombat priority window before DeclareAttackers. We drain
    /// all Priority decisions (supplying Pass) until DeclareAttackers surfaces.
    /// </summary>
    [Fact]
    public async Task SearchAgent_Capture_PausesAtDeclareAttackers_ReportsLegalMoves_ThenResumes()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.ClearSummoningSickness();

        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var aliceId = alice.Id;
        SearchAgent? captured = null;
        var sandbox = SandboxGame.From(
            new[] { alice, bob },
            new GameRandom(1),
            p => p.Id == aliceId
                ? (captured = new SearchAgent(p))
                : (IPlayerAgent)new DeterministicBotAgent());
        var agent = captured!;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // CRITICAL ORDERING: capture the first decision TCS BEFORE starting the
        // engine. ResumeAsync runs synchronously on this thread until the engine
        // hits its first await (inside DecideAsync). By that point DecideAsync has
        // already swapped _decisionReady from TCS_A → TCS_B and completed TCS_A.
        // If we called NextDecisionAsync() AFTER ResumeAsync we would see TCS_B
        // (still pending) and TCS_A's completion would be missed — the loop would
        // hang forever waiting for TCS_B while the engine waits for SupplyMove
        // on its first moveTcs. Capturing BEFORE lets Task.WhenAny see TCS_A as
        // already completed the moment we reach the first await.
        var nextDecision = agent.NextDecisionAsync();  // TCS_A — still pending

        var run = sandbox.ResumeAsync(
            PhaseStateType.Combat,
            sandbox.State.PlayerFor(alice),
            turnNumber: 3,
            maxTurns: 5,
            ct: cts.Token);
        // Engine has run synchronously to its first await (moveTcs in DecideAsync).
        // TCS_A is now completed; _decisionReady = TCS_B.

        // Drive all decisions until engine completes, surfacing each to the loop.
        // We keep a flag to verify DeclareAttackers was seen.
        bool foundDeclareAttackers = false;
        SimDecision? attackDecision = null;

        while (true)
        {
            // Wait for either the next decision or the engine to finish.
            var winner = await Task.WhenAny(nextDecision, run);

            if (ReferenceEquals(winner, run))
                break; // engine done (no more decisions needed)

            var decision = await nextDecision; // already completed (WhenAny guarantees it)

            // Capture the NEXT decision TCS BEFORE calling SupplyMove.
            // SupplyMove resumes the engine which runs into the next DecideAsync,
            // swaps _decisionReady (B→C), and completes TCS_B. If we read
            // NextDecisionAsync() AFTER SupplyMove we race with that swap and
            // might get the already-completed TCS_B or a stale task.
            nextDecision = agent.NextDecisionAsync();

            if (decision.Kind == SimDecisionKind.DeclareAttackers)
            {
                foundDeclareAttackers = true;
                attackDecision = decision;
                agent.SupplyMove(decision.LegalMoves.First(m => m.IsEmptyAttack));
            }
            else if (decision.Kind == SimDecisionKind.Priority)
            {
                agent.SupplyMove(decision.LegalMoves.First(m => m.IsPass));
            }
            else // DeclareBlockers
            {
                agent.SupplyMove(decision.LegalMoves[0]);
            }
        }

        await run.WaitAsync(TimeSpan.FromSeconds(5));

        foundDeclareAttackers.Should().BeTrue(
            because: "SearchAgent must surface a DeclareAttackers decision");
        attackDecision!.LegalMoves.Should().Contain(m => m.IsEmptyAttack,
            because: "empty-attack (pass) must always be a legal move");
        attackDecision!.LegalMoves.Should().Contain(m => m.IsAllOutAttack,
            because: "attacking with the only eligible creature must appear");
    }
}
