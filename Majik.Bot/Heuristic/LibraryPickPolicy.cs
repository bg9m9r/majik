using Majik.Bot.Evaluation;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Picks a library card for tutor effects (CR 701.19a — Demonic Tutor,
/// Worldly Tutor, fetch-style searches, etc.). The interface default
/// returns the first candidate; this policy scores each candidate against
/// the caster's current board/hand state and the configured
/// <see cref="ArchetypeWeights"/>, then returns the highest-EV pick.
///
/// Scoring derives from <paramref name="self"/>'s zones (hand,
/// battlefield, library), the candidate card's printed attributes, and —
/// when a <see cref="GameContext"/> is threaded through (v2,
/// <c>SearchSpellFactory</c> builds one from
/// <see cref="Majik.Core.Game.ChosenSpellParams.AllPlayers"/>) — the
/// opponent's board state. Call sites that still pass <c>ctx == null</c>
/// (e.g. tests, older effect closures) degrade gracefully: the
/// opp-driven heuristics no-op, archetype + curve-fit signals still
/// drive the pick. Scoring rewards:
/// <list type="bullet">
///   <item>Lands when the caster is mana-screwed (curve gap).</item>
///   <item>Threats when the opponent has a heavier board.</item>
///   <item>Card draw when hand is low.</item>
///   <item>Removal-shaped spells when opponent has a big threat in play.</item>
///   <item>Archetype curve fit — Burn wants cheap burn, Prowess wants
///   tempo creatures, BorosEnergy wants midrange payoffs.</item>
/// </list>
///
/// Mirrors the structure of <see cref="TargetPolicy"/> and
/// <see cref="ModalPolicy"/> — pure scoring helpers, no engine mutation.
/// </summary>
public static class LibraryPickPolicy
{
    public static ICard? Pick(
        Player self,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        ArchetypeWeights weights,
        GameContext? ctx = null)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        // Snapshot caller-side state once; reused across scoring passes.
        var ctxs = BuildContext(self, ctx);

        ICard? best = null;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var s = Score(c, ctxs, weights);
            // Stable tie-break: first candidate wins on equal score (matches
            // the legacy "first candidate" default).
            if (s > bestScore)
            {
                bestScore = s;
                best = c;
            }
        }
        return best ?? candidates[0];
    }

    /// <summary>
    /// Cached per-call snapshot of the caster's situational pressure. Pure;
    /// computed once per Pick call to avoid recomputing inside the
    /// per-candidate scoring loop.
    /// </summary>
    private readonly record struct PickContext(
        int Lands,
        int HandSize,
        int LandsInHand,
        int OwnBoardPower,
        int OppBoardMaxPower,
        bool ManaScrewed,
        bool LowHand,
        bool BoardBehind,
        bool OppHasBigThreat);

    private static PickContext BuildContext(Player self, GameContext? ctx)
    {
        var battlefield = self.Zones.Battlefield.GetCards().ToList();
        var hand = self.Zones.Hand.GetCards().ToList();
        var lands = battlefield.Count(c => c is Land);
        var landsInHand = hand.Count(c => c is Land);
        var ownPower = battlefield.OfType<Creature>().Sum(c => Math.Max(0, c.Power));

        // Opponent inspection: when a GameContext is threaded through
        // (SearchSpellFactory builds one from ChosenSpellParams.AllPlayers),
        // enumerate every non-self player's battlefield and snapshot their
        // biggest creature. Older call paths still pass ctx == null —
        // ProbeOpponent returns neutral defaults so archetype + curve-fit
        // signals still drive the pick.
        var (oppMaxPower, oppHasBig) = ProbeOpponent(self, ctx);

        var manaScrewed = lands <= 2 && landsInHand == 0;
        var lowHand = hand.Count <= 1;
        var boardBehind = oppMaxPower > 0 && ownPower < oppMaxPower;

        return new PickContext(
            Lands: lands,
            HandSize: hand.Count,
            LandsInHand: landsInHand,
            OwnBoardPower: ownPower,
            OppBoardMaxPower: oppMaxPower,
            ManaScrewed: manaScrewed,
            LowHand: lowHand,
            BoardBehind: boardBehind,
            OppHasBigThreat: oppHasBig);
    }

    /// <summary>
    /// Opponent probe. When <paramref name="ctx"/> is non-null the policy
    /// walks every non-self player on the roster and snapshots the biggest
    /// creature in play — that's enough signal to bias toward removal /
    /// blockers when an opp threat is on the battlefield. When ctx is
    /// null (legacy call paths that don't yet pass one) return neutral
    /// defaults so opp-driven heuristics no-op gracefully.
    /// </summary>
    private static (int oppMaxPower, bool oppHasBig) ProbeOpponent(Player self, GameContext? ctx)
    {
        if (ctx == null) return (0, false);

        int maxPower = 0;
        foreach (var p in ctx.AllPlayers)
        {
            if (ReferenceEquals(p, self)) continue;
            foreach (var c in p.Zones.Battlefield.GetCards())
            {
                if (c is Creature crt && crt.Power > maxPower)
                    maxPower = crt.Power;
            }
        }
        // "Big" = 4-power creature (canonical "must-answer" threshold —
        // matches the same 4-power cutoff used as the KeyCardInPlay bump
        // in Score below).
        return (maxPower, maxPower >= 4);
    }

    /// <summary>
    /// Score a single candidate. Higher = better pick. Combines:
    ///   * Card-shape value (creature stats, removal verbs, draw verbs)
    ///   * Situational urgency (mana-screw → lands, low hand → draw, ...)
    ///   * Archetype weight bias (Burn loves cheap burn, Prowess loves
    ///     tempo creatures, BorosEnergy loves payoffs)
    /// </summary>
    private static double Score(ICard c, PickContext px, ArchetypeWeights w)
    {
        double s = 0.0;
        var cmc = ApproxCmc(c);

        // ---- Card-shape value -------------------------------------------------
        if (c is Creature crt)
        {
            // Creatures contribute power + toughness scaled by archetype.
            s += w.BoardPower * Math.Max(0, crt.Power);
            s += w.BoardToughness * Math.Max(0, crt.Toughness);
            if (crt.Power >= 4) s += w.KeyCardInPlay;
            // Cheap creatures fit Prowess/Burn curves better.
            if (cmc <= 2) s += w.Tempo * 0.5;
        }
        else if (c is Land)
        {
            // Lands fix mana — value scales with how mana-screwed we are.
            // The mana-screw multiplier is large because the alternative is
            // not casting any spell next turn at all; an extra land in a
            // flooded game is almost worthless. Lands cost 0 mana so they
            // never trip the curve-ceiling penalty.
            s += w.ManaSources * (px.ManaScrewed ? 20.0 : 1.0);
        }
        else if (c is Permanent)
        {
            // Artifact / Enchantment / Planeswalker.
            s += w.Tempo * 0.5 + w.KeyCardInPlay * 0.25;
        }
        else
        {
            // Instant / Sorcery — score by printed-text shape.
            s += ScoreSpellByShape(c, w, px);
        }

        // ---- Curve fit --------------------------------------------------------
        // Penalise candidates we can't cast soon. Match curve to current
        // mana-source count + lands-in-hand so we still pick reasonable
        // ramp targets when we have one drop banked. Penalty scales
        // super-linearly so 10-mana bombs lose decisively to 2-drops when
        // we only have 1 land — uncastable cards are dead cards.
        var castableCeiling = px.Lands + px.LandsInHand + 1;
        if (cmc > castableCeiling)
        {
            var gap = cmc - castableCeiling;
            s -= gap * gap * 2.0;
        }

        // ---- Situational pressure --------------------------------------------
        if (px.LowHand && LooksLikeCardDraw(c)) s += w.HandSize * 3.0;
        if (px.BoardBehind && c is Creature behindCrt && behindCrt.Power >= 3) s += w.BoardPower * 1.5;
        if (px.OppHasBigThreat && LooksLikeRemoval(c)) s += Math.Abs(w.OpponentThreats) * 2.0;

        return s;
    }

    /// <summary>
    /// Token-bag score over the printed ability text for instants / sorceries.
    /// Mirrors <see cref="ModalPolicy"/>'s mode-text scorer in spirit; we
    /// can't read OracleText on ICard (lives on the DB entity), so we
    /// inspect the card's parsed abilities. KeywordAbility names give a
    /// proxy for "what this spell does".
    /// </summary>
    private static double ScoreSpellByShape(ICard c, ArchetypeWeights w, PickContext px)
    {
        double s = 0.0;
        // Default tempo credit so any instant/sorcery beats "nothing".
        s += w.Tempo * Math.Max(1, ApproxCmc(c));

        if (LooksLikeRemoval(c)) s += Math.Abs(w.OpponentThreats) * 1.0;
        if (LooksLikeBurn(c)) s += w.LifeDelta * 1.5;
        if (LooksLikeCardDraw(c)) s += w.HandSize * 1.5;

        return s;
    }

    /// <summary>
    /// Heuristic: card has at least one ability whose printed name maps
    /// to a removal-style verb. Engine doesn't expose oracle text on the
    /// ICard surface, so we scan keyword abilities + ability descriptions.
    /// </summary>
    private static bool LooksLikeRemoval(ICard c)
        => HasAbilityTextMatching(c, "destroy", "exile", "deal", "damage to target");

    private static bool LooksLikeBurn(ICard c)
        => HasAbilityTextMatching(c, "deal", "damage to any target", "damage to target player");

    private static bool LooksLikeCardDraw(ICard c)
        => HasAbilityTextMatching(c, "draw");

    private static bool HasAbilityTextMatching(ICard c, params string[] needles)
    {
        foreach (var ab in c.Abilities)
        {
            // KeywordAbility has a Keyword string. Other ability types fall
            // back to their ToString — best-effort, matches the "scan
            // surface text" approach used by ModalPolicy.
            var probe = ab is KeywordAbility k ? k.Keyword : ab.GetType().Name;
            if (string.IsNullOrEmpty(probe)) continue;
            var lower = probe.ToLowerInvariant();
            foreach (var n in needles)
                if (lower.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int ApproxCmc(ICard card)
        => ManaCost.Parse(card.ManaCost ?? string.Empty).TotalValue;
}
