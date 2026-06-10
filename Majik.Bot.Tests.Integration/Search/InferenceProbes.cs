using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

// ═══════════════════════════════════════════════════════════════════════════════
// ASYMMETRIC inference strength probes — interpretation guide
// ═══════════════════════════════════════════════════════════════════════════════
//
// On-demand strength probes for the honest-vs-human INFERENCE bot on an
// ASYMMETRIC matchup. One head-to-head per class (each class = its own xUnit
// collection → un-skipping several runs them in PARALLEL; searches are
// iteration-bound so contention cannot distort the numbers — see
// ProbeHarness.MctsBudgetMs).
//
// Determinization (the mirror probes in DeterminizedProbes.cs) removes the
// perfect-info PEEK when the opponent's archetype is KNOWN and named via
// BotConfig.OpponentArchetype. INFERENCE goes one step further: with
// BotConfig.InferOpponentArchetype = true (and OpponentArchetype null,
// Strategy = "mcts") the bot does NOT know the opponent's deck. It reads the
// opponent's PUBLIC cards from the live GameContext, infers a normalized belief
// over the curated archetypes (ArchetypeInferencer), allocates the determinized
// worlds across that belief (WorldAllocator), and runs belief-driven
// determinized search (DeterminizedSearch.RunBelief). This is honest play — no
// peek — and, crucially, no assumption: the opponent's deck is inferred, not
// handed to the bot.
//
// WHY ASYMMETRIC. The determinization probes run a MIRROR (both seats the same
// archetype), so even a wrong/over-confident sampler tends to land near the
// truth — the opponent really IS the archetype the bot would guess. These
// probes are the harder, more honest test: the bot under test plays one
// archetype (Prowess) while the opponent plays a DIFFERENT one (Burn) that the
// bot must correctly INFER from public cards. A broken inferencer (or
// wrong-deck sampling) shows up here as a strength crater that the mirror
// would mask.
//
// THE FOUR HEAD-TO-HEADS (one class each):
//   1. InferVsHeuristicProbe — inference bot (Prowess, honest, infers Burn) vs
//      heuristic Burn. The headline: honest inference vs a baseline opponent.
//   2. InferVsPerfectInfoProbe — inference bot (honest) vs perfect-info MCTS
//      Burn (peeks at real hidden zones when it clones the state). Honest
//      inference against an opponent that cheats.
//   3. PerfectInfoVsHeuristicProbe — perfect-info MCTS Prowess (peeks, no
//      inference) vs heuristic Burn. The ceiling the inference number is read
//      against: how strong the same search is on this matchup when it cheats.
//   4. KnownDetVsHeuristicProbe — determinized Prowess TOLD the opponent is
//      Burn (honest, no belief spread) vs heuristic Burn. Diagnostic: isolates
//      the no-peek honesty cost from inference quality. If this beats heuristic
//      clearly while (1) lags, the gap is inference quality (wrong-archetype
//      worlds); if this ≈ (1), the gap is just the price of not peeking.
//
// INTERPRETATION (no hard win-rate assertion — liveness only):
//   • Inference should still beat the heuristic CLEARLY. The determinization
//     mirror baseline was ≈75–79% det-vs-heuristic; the asymmetric inference
//     number is expected in the same neighbourhood (a modest honest dip is
//     fine — the bot pays for honesty + the inference step).
//   • Inference-vs-perfect-info around 50%, or a MODEST dip below it, is
//     ACCEPTABLE and EXPECTED — honest play giving up a cheat against an
//     opponent that still peeks.
//   • A CRATER — inference-vs-heuristic well below the
//     perfect-info-vs-heuristic baseline (e.g. <40% when perfect-info wins
//     70%+) — signals a real problem: bad inference (it never identifies
//     Burn), wrong-deck sampling, K too low, or per-world budget starvation.
//     That is the controller's judgment call from the logged [INFER] lines,
//     not a test failure.
//   • Because the head-to-heads now run as SEPARATE probes (possibly in
//     parallel, iteration-bound at the generous wall-clock), read each run
//     against ITS OWN PerfectInfoVsHeuristicProbe baseline — numbers are not
//     directly comparable to the old 1500 ms wall-clock-bound monolithic runs.
//
// Each probe asserts liveness only (≥1 decided game, win-rate in [0,1]); the
// controller un-skips, tails /tmp/majik-probe-progress.log, and greps the
// [INFER] / [STRENGTH] lines. Shared loop/config/consts: ProbeHarness.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ASYMMETRIC head-to-head 1: Inference Prowess (honest, infers Burn) vs
/// Heuristic Burn. See the interpretation guide at the top of this file and
/// <see cref="ProbeHarness"/>.
/// </summary>
public sealed class InferVsHeuristicProbe
{
    private readonly ITestOutputHelper _out;

    public InferVsHeuristicProbe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task Infer_vs_Heuristic()
    {
        const string label = "infer-vs-heuristic";

        var r = await ProbeHarness.RunHeadToHead(
            _out, label: label,
            seatA: ProbeHarness.Inference,    seatADeck: ProbeHarness.AsymmetricBotDeck,
            seatB: ProbeHarness.HeuristicOpp, seatBDeck: ProbeHarness.AsymmetricOppDeck,
            seedBlock: ProbeHarness.AsymmetricBaseSeed);

        ProbeHarness.LogSummary(_out, tag: "[INFER]", label: label,
            decks: $"botDeck={ProbeHarness.AsymmetricBotDeck} oppDeck={ProbeHarness.AsymmetricOppDeck}", r: r);
        ProbeHarness.AssertLiveness(label, r);
    }
}

/// <summary>
/// ASYMMETRIC head-to-head 2: Inference Prowess (honest, infers Burn) vs
/// Perfect-info MCTS Burn (peeks). See the interpretation guide at the top of
/// this file and <see cref="ProbeHarness"/>.
/// </summary>
public sealed class InferVsPerfectInfoProbe
{
    private readonly ITestOutputHelper _out;

    public InferVsPerfectInfoProbe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task Infer_vs_PerfectInfo()
    {
        const string label = "infer-vs-perfectinfo";

        var r = await ProbeHarness.RunHeadToHead(
            _out, label: label,
            seatA: ProbeHarness.Inference,      seatADeck: ProbeHarness.AsymmetricBotDeck,
            seatB: ProbeHarness.PerfectInfoOpp, seatBDeck: ProbeHarness.AsymmetricOppDeck,
            seedBlock: ProbeHarness.AsymmetricBaseSeed + 1000);

        ProbeHarness.LogSummary(_out, tag: "[INFER]", label: label,
            decks: $"botDeck={ProbeHarness.AsymmetricBotDeck} oppDeck={ProbeHarness.AsymmetricOppDeck}", r: r);
        ProbeHarness.AssertLiveness(label, r);
    }
}

/// <summary>
/// ASYMMETRIC head-to-head 3 (baseline): Perfect-info MCTS Prowess (peeks, no
/// inference) vs Heuristic Burn — the ceiling the inference numbers are read
/// against. See the interpretation guide at the top of this file and
/// <see cref="ProbeHarness"/>. (The MIRROR family has its own baseline:
/// <see cref="MirrorPerfectInfoVsHeuristicProbe"/>.)
/// </summary>
public sealed class PerfectInfoVsHeuristicProbe
{
    private readonly ITestOutputHelper _out;

    public PerfectInfoVsHeuristicProbe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task PerfectInfo_vs_Heuristic()
    {
        const string label = "perfectinfo-vs-heuristic";

        var r = await ProbeHarness.RunHeadToHead(
            _out, label: label,
            seatA: ProbeHarness.PerfectInfoBot, seatADeck: ProbeHarness.AsymmetricBotDeck,
            seatB: ProbeHarness.HeuristicOpp,   seatBDeck: ProbeHarness.AsymmetricOppDeck,
            seedBlock: ProbeHarness.AsymmetricBaseSeed + 2000);

        ProbeHarness.LogSummary(_out, tag: "[INFER]", label: label,
            decks: $"botDeck={ProbeHarness.AsymmetricBotDeck} oppDeck={ProbeHarness.AsymmetricOppDeck}", r: r);
        ProbeHarness.AssertLiveness(label, r);
    }
}

/// <summary>
/// ASYMMETRIC head-to-head 4 (diagnostic): Known-Burn Determinized Prowess
/// (honest, told the opponent is Burn — no belief spread) vs Heuristic Burn.
/// Isolates the no-peek honesty cost from inference quality. See the
/// interpretation guide at the top of this file and <see cref="ProbeHarness"/>.
/// </summary>
public sealed class KnownDetVsHeuristicProbe
{
    private readonly ITestOutputHelper _out;

    public KnownDetVsHeuristicProbe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task KnownDet_vs_Heuristic()
    {
        const string label = "knowndet-vs-heuristic";

        var r = await ProbeHarness.RunHeadToHead(
            _out, label: label,
            seatA: ProbeHarness.KnownDeterminized, seatADeck: ProbeHarness.AsymmetricBotDeck,
            seatB: ProbeHarness.HeuristicOpp,      seatBDeck: ProbeHarness.AsymmetricOppDeck,
            seedBlock: ProbeHarness.AsymmetricBaseSeed + 3000);

        ProbeHarness.LogSummary(_out, tag: "[INFER]", label: label,
            decks: $"botDeck={ProbeHarness.AsymmetricBotDeck} oppDeck={ProbeHarness.AsymmetricOppDeck}", r: r);
        ProbeHarness.AssertLiveness(label, r);
    }
}
