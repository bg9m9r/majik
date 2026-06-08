using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using System.Threading;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Regression guard for the MCTS search-loop deadlock that manifests
/// under thread-pool starvation on machines with 1–2 available cores.
///
/// <para>
/// <b>Root cause (fixed):</b> <see cref="EngineSimulator.Advance"/> and
/// <see cref="EngineSimulator.Rollout"/> call
/// <see cref="Majik.Core.Simulation.SandboxGame.ResumeAsync"/> — an async
/// method whose internal <c>await PriorityRound(...)</c> calls (in
/// <c>TurnDriver</c>) capture whatever <see cref="SynchronizationContext"/>
/// is installed on the calling thread. When driven from an xUnit test worker
/// (which installs <c>MaxConcurrencySyncContext</c>), those continuations
/// were posted to xUnit's bounded-thread-pool queue. If both xUnit workers
/// were already blocked at <c>GetResult()</c> inside the search loop, the
/// queued continuations never ran → deadlock.
/// </para>
///
/// <para>
/// <b>Fix:</b> <see cref="EngineSimulator"/> wraps every
/// <c>AdvanceCoreUnsafe</c> / <c>RolloutCoreUnsafe</c> call with
/// <c>WithNullSyncContext</c>, which temporarily clears
/// <see cref="SynchronizationContext.Current"/>. With a null context every
/// engine <c>await</c> sets up its continuation to run inline on the
/// completing thread — no scheduler post, no thread-pool dependency.
/// </para>
///
/// <para>
/// <b>Regression guard strategy:</b> xUnit's parallel test runner installs
/// <c>MaxConcurrencySyncContext</c> on every worker. We simulate the
/// worst-case environment by:
/// <list type="bullet">
///   <item>Running the guard itself via xUnit (context already installed).</item>
///   <item>Pinning the test process to 2 logical cores via
///     <c>taskset -c 0,1</c> in CI (see the CI run command). The test itself
///     cannot call <c>taskset</c>, but the hang-timeout catches any regression:
///     a deadlock would cause the test to hang &gt; 15 s and fail.</item>
///   <item>Verifying that both <see cref="EngineSimulator.Advance"/> and
///     <see cref="EngineSimulator.Rollout"/> complete within a hard 15-second
///     wall-clock deadline, which is far shorter than the 120 s blame-hang
///     timeout used in CI.</item>
/// </list>
/// </para>
/// </summary>
public class ConcurrencyRegressionTests
{
    /// <summary>
    /// Board position: Alice has two 2/2 bears ready; Bob has 3 life and no blockers.
    /// Calling Advance then two Rollout calls exercises the full TCS handshake
    /// that previously deadlocked under pool starvation.
    /// The test asserts correctness (correct decision kind, swing beats pass)
    /// AND a wall-clock timeout to catch any future hang regression.
    /// </summary>
    [Fact(Timeout = 15_000 /* ms — any hang is a regression */)]
    public void EngineSimulator_Advance_And_Rollout_CompleteWithoutDeadlock_UnderPoolPressure()
    {
        // ── Board setup ──────────────────────────────────────────────────────
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   3);

        foreach (var name in new[] { "BearA", "BearB" })
        {
            var c = new Creature(name, "{1}{G}", 2, 2);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
        }

        // Pad libraries so the engine never draw-loses during simulation.
        for (int i = 0; i < 15; i++)
        {
            var fa = new Land("Forest"); fa.ChangeOwner(alice); alice.Zones.GetZone(ZoneType.Library).AddCard(fa);
            var fb = new Land("Forest"); fb.ChangeOwner(bob);   bob.Zones.GetZone(ZoneType.Library).AddCard(fb);
        }

        var root = SimState.Capture(
            new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            phase: PhaseStateType.Combat,
            searchedSeat: alice);

        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));

        // ── Advance ──────────────────────────────────────────────────────────
        var decision = sim.Advance(root, Array.Empty<SimMove>());

        decision.IsTerminal.Should().BeFalse();
        decision.Kind.Should().Be(SimDecisionKind.DeclareAttackers);
        decision.LegalMoves.Should().Contain(m => m.IsAllOutAttack, "2 bears are eligible attackers");
        decision.LegalMoves.Should().Contain(m => m.IsEmptyAttack,  "no-attack is always legal");

        // ── Rollout — swing (wins this turn) should beat pass ────────────────
        var allOut = decision.LegalMoves.First(m => m.IsAllOutAttack);
        var pass   = decision.LegalMoves.First(m => m.IsEmptyAttack);

        // depthTurns=0: only finish the current partial turn (no extra turns).
        // 2×2/2 deals 4 damage to Bob (3 life) → he dies → WinValue.
        // Passing means no combat → turn ends, both alive → lower BoardEval score.
        var swingValue = sim.Rollout(root, new[] { allOut }, depthTurns: 0);
        var passValue  = sim.Rollout(root, new[] { pass },   depthTurns: 0);

        swingValue.Should().BeGreaterThan(passValue,
            because: "swinging 4 damage into Bob's 3 life wins immediately, " +
                     "which must outvalue doing nothing");
    }

    /// <summary>
    /// Stress variant: run Advance back-to-back many times on a blocked thread
    /// to amplify any pool-starvation or TCS rotation bug. A single hung call
    /// would cause the [Fact(Timeout=15_000)] to fail.
    /// </summary>
    [Fact(Timeout = 30_000 /* ms */)]
    public void EngineSimulator_Advance_RepeatedCalls_NeverDeadlock()
    {
        const int Iterations = 20;

        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   3);

        foreach (var name in new[] { "BearA", "BearB" })
        {
            var c = new Creature(name, "{1}{G}", 2, 2);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
        }

        for (int i = 0; i < 20; i++)
        {
            var fa = new Land("Forest"); fa.ChangeOwner(alice); alice.Zones.GetZone(ZoneType.Library).AddCard(fa);
            var fb = new Land("Forest"); fb.ChangeOwner(bob);   bob.Zones.GetZone(ZoneType.Library).AddCard(fb);
        }

        var root = SimState.Capture(
            new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            phase: PhaseStateType.Combat,
            searchedSeat: alice);

        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));

        for (int i = 0; i < Iterations; i++)
        {
            var d = sim.Advance(root, Array.Empty<SimMove>());
            d.IsTerminal.Should().BeFalse($"iteration {i}");
            d.Kind.Should().Be(SimDecisionKind.DeclareAttackers, $"iteration {i}");
        }
    }
}
