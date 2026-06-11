using Xunit;
using Xunit.Abstractions;

namespace Majik.Bot.Tests.Integration.Search;

// ═══════════════════════════════════════════════════════════════════════════════
// ASYMMETRIC heuristic-vs-heuristic ANCHOR — interpretation guide
// ═══════════════════════════════════════════════════════════════════════════════
//
// Every asymmetric strength head in InferenceProbes.cs puts an MCTS-family bot
// on the Prowess seat against a heuristic Burn opponent. Those numbers conflate
// two things: how good the SEARCH is, and how good the MATCHUP is (Prowess may
// simply lose to a functional Burn deck regardless of pilot). This anchor
// separates them: SAME decks, SAME seat alternation, SAME seed convention —
// but BOTH seats heuristic. The result is the matchup's intrinsic win-rate
// with the search bot out of the picture.
//
// READING IT against the MCTS asymmetric numbers:
//   • Anchor LOW (~10–25%): the asymmetric MCTS numbers in the same range are
//     mostly the MATCHUP — Prowess loses to Burn under any pilot, and the
//     search bot is roughly matching (or beating) its own heuristic on the
//     same seat. Not an MCTS finding.
//   • Anchor HIGH (~40–50%+): the matchup is winnable, so MCTS-Prowess
//     sitting at 7–27% is UNDERPERFORMING its own heuristic on the same seat
//     — a real search problem (eval, honesty cost, inference, or budget).
//
// One head-to-head, liveness-only assertion; the controller un-skips, tails
// /tmp/majik-probe-progress.log, and reads the [STRENGTH] / [ANCHOR] lines.
// Shared loop/config/consts: ProbeHarness.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ASYMMETRIC anchor: Heuristic Prowess vs Heuristic Burn — the matchup
/// baseline the MCTS asymmetric heads (<c>InferenceProbes.cs</c>) are read
/// against. See the interpretation guide at the top of this file and
/// <see cref="ProbeHarness"/>.
/// </summary>
public sealed class HeuristicAnchorProbe
{
    private readonly ITestOutputHelper _out;

    public HeuristicAnchorProbe(ITestOutputHelper output) => _out = output;

    [Fact(Skip = "on-demand strength probe — un-skip to run")]
    public async Task Heuristic_vs_Heuristic_Anchor()
    {
        const string label = "heuristic-anchor prowess-vs-burn";

        var r = await ProbeHarness.RunHeadToHead(
            _out, label: label,
            seatA: ProbeHarness.HeuristicBot, seatADeck: ProbeHarness.AsymmetricBotDeck,
            seatB: ProbeHarness.HeuristicOpp, seatBDeck: ProbeHarness.AsymmetricOppDeck,
            seedBlock: ProbeHarness.AnchorBaseSeed);

        // iterations/budgetMs are search knobs — 0/0 here: both seats heuristic.
        ProbeHarness.LogSummary(_out, tag: "[ANCHOR]", label: label,
            decks: $"botDeck={ProbeHarness.AsymmetricBotDeck} oppDeck={ProbeHarness.AsymmetricOppDeck}", r: r,
            iterations: 0, budgetMs: 0);
        ProbeHarness.AssertLiveness(label, r);
    }
}
