using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Heuristic;

/// <summary>
/// London mulligan policy (CR 103.4). Multi-axis hand evaluation:
///   * Land count (2–5 is the sweet spot)
///   * Curve health (something castable on turn 1 or 2)
///   * Color support (lands can produce the colors our nonlands need)
///   * Threat density (≥ 1 nonland besides cantrips/removal)
///
/// Thresholds loosen with each mulligan taken so the bot doesn't dig
/// itself to 4-card hands. Past 3 mulligans we always keep.
/// </summary>
public static class MulliganPolicy
{
    public static MulliganDecision Decide(IReadOnlyList<ICard> hand, int mulligansTaken)
    {
        if (mulligansTaken >= 3) return MulliganDecision.Keep;

        var lands = hand.Where(c => c is Land).ToList();
        var nonlands = hand.Where(c => c is not Land).ToList();

        // Land count gate — keep tight at 0 mulligans, loosen one notch per
        // mulligan taken. 7-card hand wants 2–5 lands; 6-card hand tolerates
        // 1–6; 5-card hand tolerates 1+.
        var minLands = mulligansTaken switch { 0 => 2, 1 => 1, _ => 1 };
        var maxLands = mulligansTaken switch { 0 => 5, 1 => 6, _ => 7 };
        if (lands.Count < minLands || lands.Count > maxLands)
            return MulliganDecision.Mulligan;

        // Need at least one nonland threat — total flood (all-land hand
        // disguised as min-lands met by zero nonlands) is still bad.
        if (nonlands.Count == 0) return MulliganDecision.Mulligan;

        // Curve check: at least one castable in turns 1–2. Skip at higher
        // mulligan counts (3-card hand often has nothing cheap).
        if (mulligansTaken == 0)
        {
            var castableEarly = nonlands.Any(c =>
                ManaCost.Parse(c.ManaCost ?? "").TotalValue <= 2);
            if (!castableEarly) return MulliganDecision.Mulligan;
        }

        // Color support: every colored cost pip in nonlands must have a
        // matching land producing that color. Allows generic-only nonlands
        // (artifacts) without color land support. Skipped at 2+ mulligans.
        if (mulligansTaken < 2 && !HasColorSupport(lands, nonlands))
            return MulliganDecision.Mulligan;

        return MulliganDecision.Keep;
    }

    private static bool HasColorSupport(IReadOnlyList<ICard> lands, IReadOnlyList<ICard> nonlands)
    {
        // Aggregate colored pips needed across the nonland hand.
        int w = 0, u = 0, b = 0, r = 0, g = 0;
        foreach (var nl in nonlands)
        {
            var cost = ManaCost.Parse(nl.ManaCost ?? "");
            w += cost.White;
            u += cost.Blue;
            b += cost.Black;
            r += cost.Red;
            g += cost.Green;
        }

        // For each color with non-zero need, the land base must contain a
        // source that produces it. Basic-subtype tag (Plains/Island/Swamp/
        // Mountain/Forest) suffices; dual lands carry two subtypes.
        if (w > 0 && !LandsProduce(lands, CardSubtype.Plains)) return false;
        if (u > 0 && !LandsProduce(lands, CardSubtype.Island)) return false;
        if (b > 0 && !LandsProduce(lands, CardSubtype.Swamp)) return false;
        if (r > 0 && !LandsProduce(lands, CardSubtype.Mountain)) return false;
        if (g > 0 && !LandsProduce(lands, CardSubtype.Forest)) return false;
        return true;
    }

    private static bool LandsProduce(IReadOnlyList<ICard> lands, CardSubtype subtype) =>
        lands.OfType<Land>().Any(l => l.Subtypes.Contains(subtype));
}
