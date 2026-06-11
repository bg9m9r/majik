using Majik.Bot.Search;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

// ═══════════════════════════════════════════════════════════════════════════════
// CAP-RAISE (iteration-cap tuning) strength probes — interpretation guide
// ═══════════════════════════════════════════════════════════════════════════════
//
// On-demand probe cells for the MCTS iteration cap (BotConfig.MaxMctsIterations;
// env Bot__MaxMctsIterations). Both cells run the LIVE production shape — reuse
// ON, wall-clock-bound at ProbeHarness.MatrixBudgetMs = 1500 ms, the live
// world split (perWorld=400, MaxWorlds=4 → K=4), mirror det-vs-heuristic — and
// differ ONLY in the iteration cap:
//
//   • cap=800 cell — today's live shipped cap (the measured 1-core reuse
//     capacity; see TreeReuseCell.MeasuredCapacityIterations). The CONTROL.
//   • cap=1200 cell — the raise hypothesis: after the allocation diet (#2609)
//     iterations cost 1.33–2.67 ms/iter, so 800-iter decisions complete in
//     ~1.1–1.3 s and the CAP binds again inside the 1500 ms budget. Does
//     letting the search spend more of the budget (up to 1200 iterations)
//     gain strength, or does the wall clock truncate it first / the extra
//     depth not matter?
//
// PAIRED SEEDS: both cells deliberately share ONE seed block
// (ProbeHarness.CapRaiseMirrorBaseSeed) — like the world-split family and
// unlike every other family's +1000-per-cell blocks — so game i in each cell
// plays the same decks / shuffles / heuristic opponent and only the iteration
// cap differs. Read the two win rates as a paired comparison.
//
// INTERPRETATION (no hard win-rate assertion — liveness only). The adoption
// gate raises the live cap to 1200 (config-only) iff the cap=1200 cell clearly
// beats the cap=800 control on the same seeds; ties or noise keep today's
// cap=800. Labels carry the full cell shape — e.g.
// "[STRENGTH] [det-vs-heuristic cap=1200 K=4 perWorld=400 reuse=on] ..." —
// so the controller can grep /tmp/majik-probe-progress.log unambiguously.
// Shared loop/config/consts: ProbeHarness.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Shared cell runner for the cap-raise (iteration-cap tuning) probe classes.</summary>
internal static class CapRaiseCell
{
    /// <summary>Determinized world split for both cells — the live shipped
    /// split (perWorld=400, MaxWorlds=4 → K=4), matching the K=4 control of
    /// the world-split family. Only the iteration cap varies across cells.</summary>
    internal const int MaxWorlds = 4;

    /// <summary>Per-world budget (ms) for both cells — the live shipped value.</summary>
    internal const int PerWorldMs = 400;

    /// <summary>
    /// MIRROR head: determinized (honest, reuse ON, 1500 ms, live world split
    /// <see cref="MaxWorlds"/>/<see cref="PerWorldMs"/>) at an explicit
    /// <paramref name="iterations"/> cap vs the pure heuristic. The label's
    /// <c>K=</c> is COMPUTED from the same <see cref="DeterminizedSearch.KFor"/>
    /// the strategy uses, so it cannot drift from the search's actual world count.
    /// </summary>
    internal static async Task RunMirror(
        ITestOutputHelper output, int iterations, int seedBlock)
    {
        int k = DeterminizedSearch.KFor(
            ProbeHarness.MatrixBudgetMs, PerWorldMs, MaxWorlds);
        var label = $"det-vs-heuristic cap={iterations} K={k} perWorld={PerWorldMs} reuse=on";

        var r = await ProbeHarness.RunHeadToHead(
            output, label: label,
            seatA: ProbeHarness.MirrorDeterminizedWorldSplitAt(
                iterations, maxWorlds: MaxWorlds, perWorldMs: PerWorldMs),
            seatADeck: ProbeHarness.MirrorArchetype,
            seatB: ProbeHarness.MirrorHeuristic,
            seatBDeck: ProbeHarness.MirrorArchetype,
            seedBlock: seedBlock);

        ProbeHarness.LogSummary(output, tag: "[DET]", label: label,
            decks: $"deck={ProbeHarness.MirrorArchetype} (mirror)", r: r,
            iterations: iterations, budgetMs: ProbeHarness.MatrixBudgetMs);
        ProbeHarness.AssertLiveness(label, r);
    }
}

// ── MIRROR head cells (det-vs-heuristic, paired seed block) ──────────────────

/// <summary>Cap-raise cell (a): cap=800 — today's live shipped cap (the
/// control; see <see cref="TreeReuseCell.MeasuredCapacityIterations"/>) at the
/// live split K=4 × perWorld=400 / 1500 ms, reuse on.</summary>
public sealed class DetVsHeuristicCap800Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicCap800Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_Cap800() =>
        CapRaiseCell.RunMirror(
            _out, iterations: TreeReuseCell.MeasuredCapacityIterations,
            seedBlock: ProbeHarness.CapRaiseMirrorBaseSeed);
}

/// <summary>Cap-raise cell (b): cap=1200 — the raise hypothesis (post-#2609
/// iterations are cheap enough that the 800 cap binds inside the 1500 ms
/// budget) at the live split K=4 × perWorld=400 / 1500 ms, reuse on (SAME
/// seed block as the cap=800 cell for a paired comparison).</summary>
public sealed class DetVsHeuristicCap1200Probe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicCap1200Probe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public Task Determinized_vs_Heuristic_Cap1200() =>
        CapRaiseCell.RunMirror(
            _out, iterations: 1200,
            seedBlock: ProbeHarness.CapRaiseMirrorBaseSeed);
}
