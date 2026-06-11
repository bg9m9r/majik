using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

// ═══════════════════════════════════════════════════════════════════════════════
// MIRROR determinization strength probes — interpretation guide
// ═══════════════════════════════════════════════════════════════════════════════
//
// On-demand strength probes for the DETERMINIZED MCTS bot on a known-decklist
// MIRROR (both seats Prowess). One head-to-head per class (each class = its own
// xUnit collection → un-skipping several runs them in PARALLEL; searches are
// iteration-bound so contention cannot distort the numbers — see
// ProbeHarness.MctsBudgetMs).
//
// Determinization removes the perfect-info PEEK (today's MCTS bot secretly
// reads the real opponent hand/library when it clones the game state). When
// BotConfig.OpponentArchetype names a known BotDeckCatalog archetype,
// SearchStrategy instead routes through DeterminizedSearch: it resamples the
// opponent's hidden zones from that decklist across K worlds, runs a
// per-world-bounded MCTS in each, and votes by summed-robust-child. This is
// honest play — no peek — at the cost of cross-world averaging.
//
// THE THREE HEAD-TO-HEADS (one class each):
//   1. DetVsHeuristicProbe — determinized MCTS (OpponentArchetype set, honest)
//      vs the pure heuristic.
//   2. DetVsPerfectInfoProbe — determinized (honest) vs perfect-info MCTS
//      (OpponentArchetype null, so it peeks). Win% for the honest bot.
//   3. MirrorPerfectInfoVsHeuristicProbe — perfect-info MCTS vs heuristic; the
//      known baseline (≈88% on Prowess per SearchVsHeuristicTests), included
//      so the determinized number is interpretable relative to it. (Named
//      "Mirror…" to avoid colliding with the asymmetric family's
//      PerfectInfoVsHeuristicProbe.)
//
// INTERPRETATION (no hard win-rate assertion — liveness only):
//   • Determinized should still beat the heuristic CLEARLY.
//   • Determinized-vs-perfect-info around 50%, or a MODEST dip below it, is
//     ACCEPTABLE and EXPECTED — honest play giving up a cheat against an
//     opponent that still peeks.
//   • A CRATER — det-vs-heuristic well below the perfect-info-vs-heuristic
//     baseline (e.g. <40% when perfect-info wins 70%+) — signals a real
//     problem: the sampler building wrong cards, K too low, or per-world
//     budget starvation. That is the controller's judgment call from the
//     logged [DET] lines, not a test failure.
//   • Because the head-to-heads now run as SEPARATE probes (possibly in
//     parallel, iteration-bound at the generous wall-clock), read each run
//     against ITS OWN MirrorPerfectInfoVsHeuristicProbe baseline — numbers are
//     not directly comparable to the old 1500 ms wall-clock-bound monolithic
//     runs.
//
// Each probe asserts liveness only (≥1 decided game, win-rate in [0,1]); the
// controller un-skips, tails /tmp/majik-probe-progress.log, and greps the
// [DET] / [STRENGTH] lines. Shared loop/config/consts: ProbeHarness.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// MIRROR head-to-head 1: Determinized MCTS (honest, OpponentArchetype set) vs
/// Heuristic — Prowess mirror. See the interpretation guide at the top of this
/// file and <see cref="ProbeHarness"/>.
/// </summary>
public sealed class DetVsHeuristicProbe
{
    private readonly ITestOutputHelper _out;

    public DetVsHeuristicProbe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task Determinized_vs_Heuristic()
    {
        const string label = "det-vs-heuristic";

        var r = await ProbeHarness.RunHeadToHead(
            _out, label: label,
            seatA: ProbeHarness.MirrorDeterminized, seatADeck: ProbeHarness.MirrorArchetype,
            seatB: ProbeHarness.MirrorHeuristic,    seatBDeck: ProbeHarness.MirrorArchetype,
            seedBlock: ProbeHarness.MirrorBaseSeed);

        ProbeHarness.LogSummary(_out, tag: "[DET]", label: label,
            decks: $"deck={ProbeHarness.MirrorArchetype} (mirror)", r: r);
        ProbeHarness.AssertLiveness(label, r);
    }
}

/// <summary>
/// MIRROR head-to-head 2: Determinized MCTS (honest) vs Perfect-info MCTS
/// (peeks) — Prowess mirror. See the interpretation guide at the top of this
/// file and <see cref="ProbeHarness"/>.
/// </summary>
public sealed class DetVsPerfectInfoProbe
{
    private readonly ITestOutputHelper _out;

    public DetVsPerfectInfoProbe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task Determinized_vs_PerfectInfo()
    {
        const string label = "det-vs-perfectinfo";

        var r = await ProbeHarness.RunHeadToHead(
            _out, label: label,
            seatA: ProbeHarness.MirrorDeterminized, seatADeck: ProbeHarness.MirrorArchetype,
            seatB: ProbeHarness.MirrorPerfectInfo,  seatBDeck: ProbeHarness.MirrorArchetype,
            seedBlock: ProbeHarness.MirrorBaseSeed + 1000);

        ProbeHarness.LogSummary(_out, tag: "[DET]", label: label,
            decks: $"deck={ProbeHarness.MirrorArchetype} (mirror)", r: r);
        ProbeHarness.AssertLiveness(label, r);
    }
}

/// <summary>
/// MIRROR head-to-head 3 (context baseline): Perfect-info MCTS (peeks) vs
/// Heuristic — Prowess mirror. Named "Mirror…" to avoid colliding with the
/// asymmetric family's <see cref="PerfectInfoVsHeuristicProbe"/>. See the
/// interpretation guide at the top of this file and <see cref="ProbeHarness"/>.
/// </summary>
public sealed class MirrorPerfectInfoVsHeuristicProbe
{
    private readonly ITestOutputHelper _out;

    public MirrorPerfectInfoVsHeuristicProbe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task PerfectInfo_vs_Heuristic_Mirror()
    {
        // Label is prefixed "mirror-" (unlike the historical monolithic run) so its
        // per-game lines can't be confused with the ASYMMETRIC family's
        // perfectinfo-vs-heuristic when all 7 probes stream to one log in parallel.
        const string label = "mirror-perfectinfo-vs-heuristic";

        var r = await ProbeHarness.RunHeadToHead(
            _out, label: label,
            seatA: ProbeHarness.MirrorPerfectInfo, seatADeck: ProbeHarness.MirrorArchetype,
            seatB: ProbeHarness.MirrorHeuristic,   seatBDeck: ProbeHarness.MirrorArchetype,
            seedBlock: ProbeHarness.MirrorBaseSeed + 2000);

        ProbeHarness.LogSummary(_out, tag: "[DET]", label: label,
            decks: $"deck={ProbeHarness.MirrorArchetype} (mirror)", r: r);
        ProbeHarness.AssertLiveness(label, r);
    }
}
