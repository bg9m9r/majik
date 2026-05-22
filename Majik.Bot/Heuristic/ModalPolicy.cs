using Majik.Bot.Evaluation;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Picks modes (CR 700.2) and X-cost values (CR 107.3). Mode picker
/// scores each mode's effect-description text against the configured
/// archetype weights using a keyword bag. X picker spends all available
/// lands.
/// </summary>
public static class ModalPolicy
{
    /// <summary>
    /// Lightweight bag-of-keywords scorer. The signature is unchanged
    /// (no <see cref="ArchetypeWeights"/> parameter) so the existing
    /// IBotStrategy contract stays intact; the scorer applies a generic
    /// "more impact = better" heuristic that approximates what every
    /// archetype wants in absence of mode metadata. Tie → first mode
    /// (legal mode 0 is always a safe fallback).
    /// </summary>
    public static int PickMode(GameContext ctx, Player self, IReadOnlyList<string> modes)
    {
        if (modes.Count == 0) return 0;

        int bestIdx = 0;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < modes.Count; i++)
        {
            var score = ScoreModeText(modes[i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    public static int PickX(GameContext ctx, Player self)
        => self.Zones.Battlefield.GetCards().OfType<Land>().Count();

    /// <summary>
    /// Token-bag heuristic over the mode's printed description.
    /// Boosts board-impacting verbs (destroy, exile, deal damage, draw)
    /// and creature-count modifiers ("X"-style multi-effects). Penalises
    /// "you lose life" / "sacrifice" / "discard" self-cost clauses. Not
    /// archetype-aware — the modes' surface text is too sparse for that
    /// in v1; ranks are stable across archetypes.
    /// </summary>
    private static double ScoreModeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0.0;
        var t = text.ToLowerInvariant();
        double s = 0.0;

        // Removal / damage are usually high-impact picks.
        if (t.Contains("destroy")) s += 3.0;
        if (t.Contains("exile")) s += 3.0;
        if (t.Contains("deal") && t.Contains("damage")) s += 2.5;
        if (t.Contains("counter target")) s += 2.5;
        if (t.Contains("return") && t.Contains("hand")) s += 1.5;
        if (t.Contains("draw")) s += 2.0;
        if (t.Contains("create") && t.Contains("token")) s += 2.0;
        if (t.Contains("gain") && t.Contains("life")) s += 1.0;
        if (t.Contains("+1/+1") || t.Contains("+2/+2")) s += 1.0;
        if (t.Contains("search")) s += 1.5;

        // Self-cost / drawback clauses penalised.
        if (t.Contains("you lose") && t.Contains("life")) s -= 2.0;
        if (t.Contains("sacrifice")) s -= 1.5;
        if (t.Contains("discard")) s -= 1.0;

        // "Each" / multi-target hits scale with breadth.
        if (t.Contains("each opponent") || t.Contains("each creature")) s += 1.0;

        // Length nudge — wordier modes tend to be the "real" effect rather
        // than vanilla "do nothing" filler.
        s += Math.Min(1.0, text.Length / 80.0);
        return s;
    }
}
