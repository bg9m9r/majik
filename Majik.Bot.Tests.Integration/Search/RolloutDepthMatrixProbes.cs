using Majik.Bot.Search;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

// ═══════════════════════════════════════════════════════════════════════════════
// ROLLOUT-DEPTH MATRIX strength probes — interpretation guide
// ═══════════════════════════════════════════════════════════════════════════════
//
// On-demand probe cells for the RolloutDepth adoption gate (the #2596
// rollout-truncation lever). Two heads × two truncated depths × two iteration
// caps = 8 cells, one xUnit class each (own collection → un-skipping several
// runs them in PARALLEL):
//
//   HEADS (the two honest production modes, each vs the pure heuristic):
//     • det-vs-heuristic   — MIRROR determinized (Prowess both seats,
//       OpponentArchetype set; tag [DET]). Mirrors DetVsHeuristicProbe.
//     • infer-vs-heuristic — ASYMMETRIC inference (Prowess bot infers the
//       Burn opponent; tag [INFER]). Mirrors InferVsHeuristicProbe.
//
//   CELLS per head (all wall-clock-BOUND at ProbeHarness.MatrixBudgetMs =
//   1500 ms, the LIVE production budget — deliberately unlike the existing
//   iteration-bound probes):
//     • LeafEval  @ 150 it — same cap as live; does pure truncation hurt?
//     • LeafEval  @ 600 it — spend the per-iteration saving on MORE
//       iterations (~1.9x cheaper per iter measured; 600 lets the budget
//       decide how many actually complete).
//     • EndOfTurn @ 150 it — same cap as live, current-turn horizon.
//     • EndOfTurn @ 300 it — more iterations (~1.6x cheaper per iter).
//
// INTERPRETATION (no hard win-rate assertion — liveness only). The Task 5
// gate adopts a truncated depth ONLY if:
//   • a more-iters cell ≥ the SAME-RUN control head at FullTurnPlus
//     (un-skip DetVsHeuristicProbe / InferVsHeuristicProbe alongside), AND
//   • the hold-back flip survives that depth — per the Task 3 pins
//     (HoldBackFlipTests.RiskSignal_PerRolloutDepth_PinnedFlipOutcome)
//     NEITHER LeafEval NOR EndOfTurn preserves the +1-turn risk signal on
//     the pinned board, so a strength win here must be weighed against
//     that documented blindness, AND
//   • masking suites stay green + the perf multiple is confirmed.
//
// Because these cells are wall-clock-bound at 1500 ms, their numbers are NOT
// comparable to the iteration-bound (6000 ms) probe family — read them only
// against control heads run at the same 1500 ms regime.
//
// Each probe asserts liveness only (≥1 decided game, win-rate in [0,1]); the
// controller un-skips, tails /tmp/majik-probe-progress.log, and greps the
// [STRENGTH] / [DET] / [INFER] lines — labels carry depth+iters, e.g.
// "[STRENGTH] [det-vs-heuristic depth=LeafEval iters=600] ...".
// Shared loop/config/consts: ProbeHarness.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Shared cell runners for the rollout-depth matrix probe classes.</summary>
internal static class RolloutDepthMatrixCell
{
    /// <summary>MIRROR head: determinized (honest, depth+iters cell) vs heuristic.</summary>
    internal static async Task RunMirror(
        ITestOutputHelper output, RolloutDepth depth, int iterations, int seedBlock)
    {
        var label = $"det-vs-heuristic depth={depth} iters={iterations}";

        var r = await ProbeHarness.RunHeadToHead(
            output, label: label,
            seatA: ProbeHarness.MirrorDeterminizedAt(depth, iterations),
            seatADeck: ProbeHarness.MirrorArchetype,
            seatB: ProbeHarness.MirrorHeuristic,
            seatBDeck: ProbeHarness.MirrorArchetype,
            seedBlock: seedBlock);

        ProbeHarness.LogSummary(output, tag: "[DET]", label: label,
            decks: $"deck={ProbeHarness.MirrorArchetype} (mirror)", r: r,
            iterations: iterations, budgetMs: ProbeHarness.MatrixBudgetMs);
        ProbeHarness.AssertLiveness(label, r);
    }

    /// <summary>ASYMMETRIC head: inference (honest, depth+iters cell) vs heuristic.</summary>
    internal static async Task RunAsymmetric(
        ITestOutputHelper output, RolloutDepth depth, int iterations, int seedBlock)
    {
        var label = $"infer-vs-heuristic depth={depth} iters={iterations}";

        var r = await ProbeHarness.RunHeadToHead(
            output, label: label,
            seatA: ProbeHarness.InferenceAt(depth, iterations),
            seatADeck: ProbeHarness.AsymmetricBotDeck,
            seatB: ProbeHarness.HeuristicOpp,
            seatBDeck: ProbeHarness.AsymmetricOppDeck,
            seedBlock: seedBlock);

        ProbeHarness.LogSummary(output, tag: "[INFER]", label: label,
            decks: $"botDeck={ProbeHarness.AsymmetricBotDeck} oppDeck={ProbeHarness.AsymmetricOppDeck}", r: r,
            iterations: iterations, budgetMs: ProbeHarness.MatrixBudgetMs);
        ProbeHarness.AssertLiveness(label, r);
    }
}

// ── MIRROR head cells (det-vs-heuristic) ─────────────────────────────────────

/// <summary>Matrix cell: determinized mirror, LeafEval @ 150 it / 1500 ms.</summary>
public sealed class DetVsHeuristicLeafEval150Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicLeafEval150Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_LeafEval_150it() =>
        RolloutDepthMatrixCell.RunMirror(
            _out, RolloutDepth.LeafEval, iterations: 150,
            seedBlock: ProbeHarness.MatrixMirrorBaseSeed);
}

/// <summary>Matrix cell: determinized mirror, LeafEval @ 600 it / 1500 ms.</summary>
public sealed class DetVsHeuristicLeafEval600Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicLeafEval600Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_LeafEval_600it() =>
        RolloutDepthMatrixCell.RunMirror(
            _out, RolloutDepth.LeafEval, iterations: 600,
            seedBlock: ProbeHarness.MatrixMirrorBaseSeed + 1000);
}

/// <summary>Matrix cell: determinized mirror, EndOfTurn @ 150 it / 1500 ms.</summary>
public sealed class DetVsHeuristicEndOfTurn150Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicEndOfTurn150Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_EndOfTurn_150it() =>
        RolloutDepthMatrixCell.RunMirror(
            _out, RolloutDepth.EndOfTurn, iterations: 150,
            seedBlock: ProbeHarness.MatrixMirrorBaseSeed + 2000);
}

/// <summary>Matrix cell: determinized mirror, EndOfTurn @ 300 it / 1500 ms.</summary>
public sealed class DetVsHeuristicEndOfTurn300Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicEndOfTurn300Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_EndOfTurn_300it() =>
        RolloutDepthMatrixCell.RunMirror(
            _out, RolloutDepth.EndOfTurn, iterations: 300,
            seedBlock: ProbeHarness.MatrixMirrorBaseSeed + 3000);
}

// ── ASYMMETRIC head cells (infer-vs-heuristic) ───────────────────────────────

/// <summary>Matrix cell: inference asymmetric, LeafEval @ 150 it / 1500 ms.</summary>
public sealed class InferVsHeuristicLeafEval150Probe
{
    private readonly ITestOutputHelper _out;

    public InferVsHeuristicLeafEval150Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Infer_vs_Heuristic_LeafEval_150it() =>
        RolloutDepthMatrixCell.RunAsymmetric(
            _out, RolloutDepth.LeafEval, iterations: 150,
            seedBlock: ProbeHarness.MatrixAsymmetricBaseSeed);
}

/// <summary>Matrix cell: inference asymmetric, LeafEval @ 600 it / 1500 ms.</summary>
public sealed class InferVsHeuristicLeafEval600Probe
{
    private readonly ITestOutputHelper _out;

    public InferVsHeuristicLeafEval600Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Infer_vs_Heuristic_LeafEval_600it() =>
        RolloutDepthMatrixCell.RunAsymmetric(
            _out, RolloutDepth.LeafEval, iterations: 600,
            seedBlock: ProbeHarness.MatrixAsymmetricBaseSeed + 1000);
}

/// <summary>Matrix cell: inference asymmetric, EndOfTurn @ 150 it / 1500 ms.</summary>
public sealed class InferVsHeuristicEndOfTurn150Probe
{
    private readonly ITestOutputHelper _out;

    public InferVsHeuristicEndOfTurn150Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Infer_vs_Heuristic_EndOfTurn_150it() =>
        RolloutDepthMatrixCell.RunAsymmetric(
            _out, RolloutDepth.EndOfTurn, iterations: 150,
            seedBlock: ProbeHarness.MatrixAsymmetricBaseSeed + 2000);
}

/// <summary>Matrix cell: inference asymmetric, EndOfTurn @ 300 it / 1500 ms.</summary>
public sealed class InferVsHeuristicEndOfTurn300Probe
{
    private readonly ITestOutputHelper _out;

    public InferVsHeuristicEndOfTurn300Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Infer_vs_Heuristic_EndOfTurn_300it() =>
        RolloutDepthMatrixCell.RunAsymmetric(
            _out, RolloutDepth.EndOfTurn, iterations: 300,
            seedBlock: ProbeHarness.MatrixAsymmetricBaseSeed + 3000);
}
