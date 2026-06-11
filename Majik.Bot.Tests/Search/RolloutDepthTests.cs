using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// The <see cref="RolloutDepth"/> knob (#2596 follow-up): rollout ENGINE DRIVE
/// is 85–90% of MCTS decision cost, so <see cref="EngineSimulator.RolloutCore"/>
/// gains three playout depths:
///
/// <list type="bullet">
///   <item><see cref="RolloutDepth.LeafEval"/> — NO playout: drive to the
///     decision point (same machinery as Advance) and return
///     <see cref="BoardEval.Score"/> there.</item>
///   <item><see cref="RolloutDepth.EndOfTurn"/> — play out the remainder of the
///     CURRENT turn only. Maps onto the existing <c>depthTurns=0</c> machinery
///     (<c>maxTurns = TurnNumber + 0</c>: the resumed partial turn always runs,
///     zero full extra turns follow).</item>
///   <item><see cref="RolloutDepth.FullTurnPlus"/> — today's behaviour
///     (<c>depthTurns</c> passes through unchanged). THE DEFAULT — zero
///     behaviour change until the probe gate picks a winner.</item>
/// </list>
/// </summary>
public sealed class RolloutDepthTests
{
    private static readonly ArchetypeWeights Weights = ArchetypeWeights.ForArchetype("Burn");

    // ── Board builders ────────────────────────────────────────────────────────

    private static void PadLibraries(params Player[] players)
    {
        foreach (var p in players)
        {
            foreach (var _ in Enumerable.Range(0, 15))
            {
                var l = new Land("Forest");
                l.ChangeOwner(p);
                p.Zones.GetZone(ZoneType.Library).AddCard(l);
            }
        }
    }

    private static Creature ReadyCreature(string name, int power, int toughness, Player owner)
    {
        var c = new Creature(name, "{2}{G}{G}", power, toughness);
        c.ChangeOwner(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.ClearSummoningSickness();
        return c;
    }

    /// <summary>
    /// Alice (searched, active) has a ready 4/4; Bob is at 3 life with an empty
    /// board. A FullTurnPlus playout demonstrably changes state: the rollout
    /// heuristic attacks with the 4/4 and the simulated game TERMINATES
    /// (+1000 win). LeafEval must instead stop at the decision point
    /// (DeclareAttackers — board untouched) and return the BoardEval score there.
    /// </summary>
    private static (Player alice, Player bob) BuildLethalSwingBoard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);
        ReadyCreature("Ox", 4, 4, alice);
        PadLibraries(alice, bob);
        return (alice, bob);
    }

    /// <summary>
    /// Alice (searched, active, at <paramref name="aliceLife"/>) has an empty
    /// board; Bob has a ready 4/4. Nothing can happen during the remainder of
    /// Alice's current turn, but on Bob's NEXT turn the heuristic opponent
    /// attacks with the 4/4 — at 3 life that kills Alice. FullTurnPlus sees the
    /// next-turn kill (-1000); EndOfTurn stops at the turn boundary and does not.
    /// </summary>
    private static (Player alice, Player bob) BuildNextTurnKillBoard(int aliceLife)
    {
        var alice = new Player("Alice", aliceLife);
        var bob = new Player("Bob", 20);
        ReadyCreature("Ox", 4, 4, bob);
        PadLibraries(alice, bob);
        return (alice, bob);
    }

    private static SimState CaptureRoot(Player alice, Player bob, PhaseStateType phase) =>
        SimState.Capture(
            new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            phase: phase,
            searchedSeat: alice);

    /// <summary>
    /// Directly-computed leaf eval at the (unchanged) root position — the same
    /// minimal-context shape <c>EngineSimulator.BuildLeafContext</c> uses
    /// (fresh empty stack; turnNumber/phase are not read by BoardEval's terms).
    /// </summary>
    private static double DirectLeafEval(Player self, params Player[] all)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(
            self: self,
            allPlayers: all,
            activePlayer: self,
            turnNumber: 0,
            currentPhase: StepStateType.PreCombatMain,
            stack: stack);
        return BoardEval.Score(ctx, self, Weights);
    }

    // ── LeafEval: no playout at all ───────────────────────────────────────────

    [Fact(Timeout = 60_000)]
    public async Task LeafEval_Rollout_PerformsNoPlayout()
    {
        await Task.Yield(); // xUnit requires async for Timeout-marked facts
        var (alice, bob) = BuildLethalSwingBoard();
        var root = CaptureRoot(alice, bob, PhaseStateType.PreCombatMain);
        var sim = new EngineSimulator(Weights);

        var leafValue = sim.Rollout(
            root, Array.Empty<SimMove>(), depthTurns: 1, rolloutDepth: RolloutDepth.LeafEval);

        // The drive to the decision point only drains pass-only priority windows
        // (no state change), so the LeafEval value IS the directly-computed
        // BoardEval score of the root position.
        var expected = DirectLeafEval(alice, alice, bob);
        leafValue.Should().Be(expected,
            "LeafEval must evaluate AT the decision point — no playout occurred");

        // Sanity: a FullTurnPlus playout on the same board visibly changes state —
        // the rollout heuristic swings the 4/4 at the 3-life opponent and WINS.
        var fullValue = sim.Rollout(
            root, Array.Empty<SimMove>(), depthTurns: 1, rolloutDepth: RolloutDepth.FullTurnPlus);
        fullValue.Should().Be(1_000.0,
            "the playout must attack for lethal — proving the playout LeafEval skipped " +
            "would have changed the value");
        leafValue.Should().NotBe(fullValue);
    }

    // ── EndOfTurn: stop at the current-turn boundary ──────────────────────────

    [Fact(Timeout = 60_000)]
    public async Task EndOfTurn_Rollout_StopsAtTurnBoundary()
    {
        await Task.Yield();
        var (alice, bob) = BuildNextTurnKillBoard(aliceLife: 3);
        var root = CaptureRoot(alice, bob, PhaseStateType.PostCombatMain);
        var sim = new EngineSimulator(Weights);

        var fullValue = sim.Rollout(
            root, Array.Empty<SimMove>(), depthTurns: 1, rolloutDepth: RolloutDepth.FullTurnPlus);
        fullValue.Should().Be(-1_000.0,
            "the turn+1 playout plays the opponent's NEXT turn, where the 4/4 kills " +
            "the 3-life searched seat");

        var endValue = sim.Rollout(
            root, Array.Empty<SimMove>(), depthTurns: 1, rolloutDepth: RolloutDepth.EndOfTurn);
        endValue.Should().NotBe(fullValue,
            "EndOfTurn must NOT see the next-turn kill");
        endValue.Should().BeGreaterThan(-1_000.0,
            "the searched seat is still alive at the end of the CURRENT turn");

        // EndOfTurn maps onto the EXISTING depthTurns=0 machinery
        // (maxTurns = TurnNumber + 0) — pin the equivalence.
        var legacyZeroDepth = sim.Rollout(root, Array.Empty<SimMove>(), depthTurns: 0);
        endValue.Should().Be(legacyZeroDepth,
            "EndOfTurn is the existing depthTurns=0 stop, regardless of the configured DepthTurns");
    }

    // ── Default: FullTurnPlus, byte-identical to today ────────────────────────

    [Fact(Timeout = 60_000)]
    public async Task Default_IsFullTurnPlus_ByteIdentical()
    {
        await Task.Yield();
        new MctsConfig().RolloutDepth.Should().Be(RolloutDepth.FullTurnPlus,
            "the config default must be today's behaviour — zero change until the gate picks");

        // Non-terminal board (the 4/4 attack only takes Alice 20 → 16) so the
        // compared values are real BoardEval doubles, not saturated ±1000.
        var (alice, bob) = BuildNextTurnKillBoard(aliceLife: 20);
        var root = CaptureRoot(alice, bob, PhaseStateType.PostCombatMain);
        var sim = new EngineSimulator(Weights);

        var defaultValue = sim.Rollout(root, Array.Empty<SimMove>(), depthTurns: 1);
        var explicitValue = sim.Rollout(
            root, Array.Empty<SimMove>(), depthTurns: 1, rolloutDepth: RolloutDepth.FullTurnPlus);

        explicitValue.Should().Be(defaultValue,
            "same board + same fixed seed: the rolloutDepth default IS FullTurnPlus");
        defaultValue.Should().NotBe(1_000.0).And.NotBe(-1_000.0,
            "this board must be non-terminal so the equality is a discriminating double, " +
            "not a saturated terminal value");
    }

    // ── Mcts threads the configured depth to the simulator ───────────────────

    [Fact]
    public void Mcts_ThreadsConfiguredRolloutDepth_ToSimulator()
    {
        var sim = new RecordingSim();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var root = SimState.Capture(
            new[] { alice, bob }, alice, 3, PhaseStateType.Combat, searchedSeat: alice);

        var mcts = new Mcts(sim, new MctsConfig(
            MaxIterations: 5,
            MaxMillis: 5_000,
            DepthTurns: 1,
            ExplorationC: 1.41,
            RolloutDepth: RolloutDepth.LeafEval));
        mcts.SearchWithStats(root);

        sim.Rollouts.Should().NotBeEmpty();
        sim.Rollouts.Should().OnlyContain(r => r.RolloutDepth == RolloutDepth.LeafEval,
            "Mcts must pass MctsConfig.RolloutDepth to every Rollout call");
        sim.Rollouts.Should().OnlyContain(r => r.DepthTurns == 1,
            "DepthTurns still rides along unchanged (the FullTurnPlus playout cap)");
    }

    /// <summary>Fake simulator recording the Rollout arguments Mcts passes.</summary>
    private sealed class RecordingSim : ISearchSimulator
    {
        public List<(int DepthTurns, RolloutDepth RolloutDepth)> Rollouts { get; } = new();

        public SimDecision Advance(SimState root, IReadOnlyList<SimMove> pathFromRoot) =>
            new(SimDecisionKind.Priority,
                new[] { SimMove.ForTest($"A@{pathFromRoot.Count}"), SimMove.ForTest($"B@{pathFromRoot.Count}") });

        public double Rollout(
            SimState root, IReadOnlyList<SimMove> pathFromRoot, int depthTurns,
            RolloutDepth rolloutDepth = RolloutDepth.FullTurnPlus)
        {
            Rollouts.Add((depthTurns, rolloutDepth));
            return 0.0;
        }
    }
}
