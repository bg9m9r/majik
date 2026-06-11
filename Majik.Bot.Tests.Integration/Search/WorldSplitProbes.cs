using Majik.Bot.Search;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

// ═══════════════════════════════════════════════════════════════════════════════
// WORLD-SPLIT (K-tuning) strength probes — interpretation guide
// ═══════════════════════════════════════════════════════════════════════════════
//
// On-demand probe cells for the determinized world-split knobs
// (BotConfig.MaxWorlds / BotConfig.PerWorldBudgetMs; env Bot__MaxWorlds /
// Bot__PerWorldBudgetMs). Both cells run the LIVE production shape — reuse ON,
// iteration cap 800, wall-clock-bound at ProbeHarness.MatrixBudgetMs = 1500 ms,
// mirror det-vs-heuristic — and differ ONLY in how that budget splits across
// determinized worlds (K = clamp(round(total / perWorld), 1, maxWorlds); the
// per-world iteration cap scales by the same perWorld/total fraction):
//
//   • K=8 cell — perWorld=200, MaxWorlds=8 → K=8 worlds × ~107 iters/world
//     (round(800×200/1500)). The MORE-WORLDS hypothesis: a 6000 ms-regime
//     head that effectively ran K=8 × few-iters scored 69% while K=4 heads
//     sat at ~40–56%, suggesting world DIVERSITY beats per-world depth now
//     that reuse makes iterations cheap.
//   • K=4 cell — perWorld=400, MaxWorlds=4 (explicit) → K=4 worlds × ~213
//     iters/world (round(800×400/1500)). The CONTROL: today's live split
//     (the shipped 800-it reuse head) expressed through the new knobs.
//
// PAIRED SEEDS: both cells deliberately share ONE seed block
// (ProbeHarness.WorldSplitMirrorBaseSeed) — unlike every other family's
// +1000-per-cell blocks — so game i in each cell plays the same decks /
// shuffles / heuristic opponent and only the world split differs. Read the
// two win rates as a paired comparison.
//
// INTERPRETATION (no hard win-rate assertion — liveness only). The adoption
// gate flips live to perWorld=200/MaxWorlds=8 (config-only) iff the K=8 cell
// clearly beats the K=4 control on the same seeds; ties or noise keep
// today's K=4 split. Labels carry the full cell shape — e.g.
// "[STRENGTH] [det-vs-heuristic K=8 perWorld=200 reuse=on iters=800] ..." —
// so the controller can grep /tmp/majik-probe-progress.log unambiguously.
// Shared loop/config/consts: ProbeHarness.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Shared cell runner for the world-split (K-tuning) probe classes.</summary>
internal static class WorldSplitCell
{
    /// <summary>Iteration cap for both cells — the live shipped value (the
    /// measured 1-core reuse capacity; see <see cref="TreeReuseCell.MeasuredCapacityIterations"/>).</summary>
    internal const int Iterations = TreeReuseCell.MeasuredCapacityIterations;

    /// <summary>
    /// MIRROR head: determinized (honest, reuse ON, cap <see cref="Iterations"/>,
    /// 1500 ms) at an explicit (<paramref name="maxWorlds"/>,
    /// <paramref name="perWorldMs"/>) world split vs the pure heuristic. The
    /// label's <c>K=</c> is COMPUTED from the same <see cref="DeterminizedSearch.KFor"/>
    /// the strategy uses, so it cannot drift from the search's actual world count.
    /// </summary>
    internal static async Task RunMirror(
        ITestOutputHelper output, int maxWorlds, int perWorldMs, int seedBlock)
    {
        int k = DeterminizedSearch.KFor(
            ProbeHarness.MatrixBudgetMs, perWorldMs, maxWorlds);
        var label = $"det-vs-heuristic K={k} perWorld={perWorldMs} reuse=on iters={Iterations}";

        var r = await ProbeHarness.RunHeadToHead(
            output, label: label,
            seatA: ProbeHarness.MirrorDeterminizedWorldSplitAt(
                Iterations, maxWorlds: maxWorlds, perWorldMs: perWorldMs),
            seatADeck: ProbeHarness.MirrorArchetype,
            seatB: ProbeHarness.MirrorHeuristic,
            seatBDeck: ProbeHarness.MirrorArchetype,
            seedBlock: seedBlock);

        ProbeHarness.LogSummary(output, tag: "[DET]", label: label,
            decks: $"deck={ProbeHarness.MirrorArchetype} (mirror)", r: r,
            iterations: Iterations, budgetMs: ProbeHarness.MatrixBudgetMs);
        ProbeHarness.AssertLiveness(label, r);
    }
}

// ── MIRROR head cells (det-vs-heuristic, paired seed block) ──────────────────

/// <summary>World-split cell (a): perWorld=200, MaxWorlds=8 → K=8 × ~107
/// iters/world @ 800 it / 1500 ms, reuse on (the more-worlds hypothesis).</summary>
public sealed class DetVsHeuristicK8Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicK8Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_K8() =>
        WorldSplitCell.RunMirror(
            _out, maxWorlds: 8, perWorldMs: 200,
            seedBlock: ProbeHarness.WorldSplitMirrorBaseSeed);
}

/// <summary>World-split cell (b): perWorld=400, MaxWorlds=4 explicit → K=4 ×
/// ~213 iters/world @ 800 it / 1500 ms, reuse on (today's live split — the
/// control; SAME seed block as the K=8 cell for a paired comparison).</summary>
public sealed class DetVsHeuristicK4Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicK4Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_K4() =>
        WorldSplitCell.RunMirror(
            _out, maxWorlds: 4, perWorldMs: 400,
            seedBlock: ProbeHarness.WorldSplitMirrorBaseSeed);
}
