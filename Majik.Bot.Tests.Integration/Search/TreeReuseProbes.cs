using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

// ═══════════════════════════════════════════════════════════════════════════════
// TREE-STATE-REUSE strength probes — interpretation guide
// ═══════════════════════════════════════════════════════════════════════════════
//
// On-demand probe cells for the tree-state-reuse adoption gate (the
// snapshot/restore lever; MctsConfig.TreeStateReuse / Bot__TreeStateReuse).
// Two heads × two iteration caps = 4 cells, one xUnit class each (own
// collection → un-skipping several runs them in PARALLEL):
//
//   HEADS (the two honest production modes, each vs the pure heuristic):
//     • det-vs-heuristic   — MIRROR determinized (Prowess both seats,
//       OpponentArchetype set; tag [DET]). Mirrors DetVsHeuristicProbe.
//     • infer-vs-heuristic — ASYMMETRIC inference (Prowess bot infers the
//       Burn opponent; tag [INFER]). Mirrors InferVsHeuristicProbe.
//
//   CELLS per head (all wall-clock-BOUND at ProbeHarness.MatrixBudgetMs =
//   1500 ms, the LIVE production budget):
//     • reuse=on @ 150 it — the LATENCY read: same iteration cap as live;
//       reuse is iteration-for-iteration EQUIVALENT (the Task 3 equivalence
//       gate), so strength must match the control head while each decision
//       finishes in ~0.6 s instead of ~1.5 s (profiler: 4.16 vs 12.35
//       ms/iter median on the pinned 1-core Release cell).
//     • reuse=on @ 800 it — the STRENGTH read: spend the saving on MORE
//       iterations. 800 is the MEASURED 1-core capacity at the live budget
//       (decisionReuse capacity cell: median 837 iters @1500 ms with reuse
//       ON vs 161 OFF — a ~5.2× multiple; 800 lets the budget decide how
//       many actually complete).
//
// INTERPRETATION (no hard win-rate assertion — liveness only). The Task 5
// gate adopts reuse for live only if:
//   • the equivalence gate (TreeReuseEquivalenceTests), the hold-back flip
//     and both masking suites stay green with reuse ON, AND
//   • the 800-it cell ≥ the SAME-RUN control head (un-skip
//     DetVsHeuristicProbe / InferVsHeuristicProbe alongside — but note those
//     run the 6000 ms iteration-bound regime; the directly comparable
//     controls are the rollout-depth matrix's 1500 ms heads), AND
//   • the 150-it cell shows no strength crater (it should be statistically
//     identical to a reuse-off 150-it head — same decisions, faster).
//
// Each probe asserts liveness only (≥1 decided game, win-rate in [0,1]); the
// controller un-skips, tails /tmp/majik-probe-progress.log, and greps the
// [STRENGTH] / [DET] / [INFER] lines — labels carry reuse+iters, e.g.
// "[STRENGTH] [det-vs-heuristic reuse=on iters=800] ...".
// Shared loop/config/consts: ProbeHarness.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Shared cell runners for the tree-state-reuse probe classes.</summary>
internal static class TreeReuseCell
{
    /// <summary>
    /// The measured 1-core iteration capacity of the live 1500 ms budget with
    /// reuse ON (decisionReuse capacity cell, Release, taskset -c 0: median
    /// 837 iters vs 161 with reuse OFF). The strength cells raise the cap
    /// here so the wall clock — not the cap — decides how many complete.
    /// </summary>
    internal const int MeasuredCapacityIterations = 800;

    /// <summary>MIRROR head: determinized (honest, reuse cell) vs heuristic.</summary>
    internal static async Task RunMirror(
        ITestOutputHelper output, int iterations, int seedBlock)
    {
        var label = $"det-vs-heuristic reuse=on iters={iterations}";

        var r = await ProbeHarness.RunHeadToHead(
            output, label: label,
            seatA: ProbeHarness.MirrorDeterminizedReuseAt(iterations),
            seatADeck: ProbeHarness.MirrorArchetype,
            seatB: ProbeHarness.MirrorHeuristic,
            seatBDeck: ProbeHarness.MirrorArchetype,
            seedBlock: seedBlock);

        ProbeHarness.LogSummary(output, tag: "[DET]", label: label,
            decks: $"deck={ProbeHarness.MirrorArchetype} (mirror)", r: r,
            iterations: iterations, budgetMs: ProbeHarness.MatrixBudgetMs);
        ProbeHarness.AssertLiveness(label, r);
    }

    /// <summary>ASYMMETRIC head: inference (honest, reuse cell) vs heuristic.</summary>
    internal static async Task RunAsymmetric(
        ITestOutputHelper output, int iterations, int seedBlock)
    {
        var label = $"infer-vs-heuristic reuse=on iters={iterations}";

        var r = await ProbeHarness.RunHeadToHead(
            output, label: label,
            seatA: ProbeHarness.InferenceReuseAt(iterations),
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

/// <summary>Reuse cell: determinized mirror, reuse=on @ 150 it / 1500 ms (latency read).</summary>
public sealed class DetVsHeuristicReuse150Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicReuse150Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_ReuseOn_150it() =>
        TreeReuseCell.RunMirror(
            _out, iterations: 150,
            seedBlock: ProbeHarness.ReuseMirrorBaseSeed);
}

/// <summary>Reuse cell: determinized mirror, reuse=on @ 800 it / 1500 ms (strength read).</summary>
public sealed class DetVsHeuristicReuse800Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicReuse800Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_ReuseOn_800it() =>
        TreeReuseCell.RunMirror(
            _out, iterations: TreeReuseCell.MeasuredCapacityIterations,
            seedBlock: ProbeHarness.ReuseMirrorBaseSeed + 1000);
}

// ── ASYMMETRIC head cells (infer-vs-heuristic) ───────────────────────────────

/// <summary>Reuse cell: inference asymmetric, reuse=on @ 150 it / 1500 ms (latency read).</summary>
public sealed class InferVsHeuristicReuse150Probe
{
    private readonly ITestOutputHelper _out;

    public InferVsHeuristicReuse150Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Infer_vs_Heuristic_ReuseOn_150it() =>
        TreeReuseCell.RunAsymmetric(
            _out, iterations: 150,
            seedBlock: ProbeHarness.ReuseAsymmetricBaseSeed);
}

/// <summary>Reuse cell: inference asymmetric, reuse=on @ 800 it / 1500 ms (strength read).</summary>
public sealed class InferVsHeuristicReuse800Probe
{
    private readonly ITestOutputHelper _out;

    public InferVsHeuristicReuse800Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Infer_vs_Heuristic_ReuseOn_800it() =>
        TreeReuseCell.RunAsymmetric(
            _out, iterations: TreeReuseCell.MeasuredCapacityIterations,
            seedBlock: ProbeHarness.ReuseAsymmetricBaseSeed + 1000);
}
