using Majik.Bot.Decks;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Strategies;

/// <summary>
/// Deck strategy for Grixis Reanimator (UBR).
///
/// // Primer: bin a fatty (Archon of Cruelty) with Faithless Looting / Thought
/// // Scour / Psychic Frog, then rebuy Emperor of Bones (CMC 2) via Persist or
/// // Unearth; Emperor exiles Archon from the graveyard (begin-of-combat
/// // trigger), adapts ({1}{B}) to gain a +1/+1 counter, and the counter
/// // trigger returns Archon to the battlefield under our control for a
/// // massive tempo swing.
/// </summary>
[DeckStrategy("GrixisReanimator")]
internal sealed class GrixisReanimatorStrategy : IDeckStrategy
{
    // ── Key card names ──────────────────────────────────────────────────────

    // Reanimation spells (CMC ≤ 3 creatures — hits Emperor of Bones / Psychic Frog)
    private const string PersistSpell = "Persist";
    private const string UnearthSpell = "Unearth";

    // Engine creature — CMC 2, valid Persist/Unearth target; its triggered
    // ability chain is what eventually returns Archon.
    private const string EmperorOfBones = "Emperor of Bones";

    // The deck's primary fatty / payoff.
    private const string ArchonOfCruelty = "Archon of Cruelty";

    // Graveyard-fill enablers.
    private const string FaithlessLooting = "Faithless Looting";
    private const string ThoughtScour = "Thought Scour";
    private const string PsychicFrog = "Psychic Frog";

    // ── IDeckStrategy.ReferencedCardNames ───────────────────────────────────

    /// <summary>
    /// Every card name referenced in strategy logic. Validated at test time
    /// against <see cref="BotDeckCatalog.Get"/>("GrixisReanimator").
    /// </summary>
    public IReadOnlyList<string> ReferencedCardNames { get; } = new[]
    {
        PersistSpell,
        UnearthSpell,
        EmperorOfBones,
        ArchonOfCruelty,
        FaithlessLooting,
        ThoughtScour,
        PsychicFrog,
    };

    // ── StrategicScore ──────────────────────────────────────────────────────

    /// <summary>
    /// Scores board progress toward the reanimator plan:
    ///
    /// <list type="bullet">
    ///   <item>+3.0 — Archon of Cruelty is in the graveyard (prime reanimate
    ///     target assembled).</item>
    ///   <item>+1.5 — Persist or Unearth is in hand (reanimation spell
    ///     available).</item>
    ///   <item>+1.0 — Emperor of Bones is in the graveyard (valid Persist/
    ///     Unearth target; engine creature for the full chain).</item>
    ///   <item>+0.5 — a graveyard-fill enabler (Faithless Looting, Thought
    ///     Scour, Psychic Frog) is in hand (setup piece available).</item>
    /// </list>
    ///
    /// These bonuses fold into MCTS BoardEval at rollout leaves to steer the
    /// search toward assembling the engine rather than random creature beats.
    /// </summary>
    public double StrategicScore(GameContext ctx, Player self)
    {
        double score = 0;

        // Primary payoff in graveyard — highest bonus.
        if (DeckStrategyHelpers.HasInGraveyard(self, ArchonOfCruelty))
            score += 3.0;

        // Reanimation spell in hand — executable line partially assembled.
        if (DeckStrategyHelpers.HasInHand(self, PersistSpell)
            || DeckStrategyHelpers.HasInHand(self, UnearthSpell))
            score += 1.5;

        // Engine creature in graveyard — valid Persist/Unearth target.
        if (DeckStrategyHelpers.HasInGraveyard(self, EmperorOfBones))
            score += 1.0;

        // Graveyard-fill enabler in hand — setup progressing.
        if (DeckStrategyHelpers.HasInHand(self, FaithlessLooting)
            || DeckStrategyHelpers.HasInHand(self, ThoughtScour)
            || DeckStrategyHelpers.HasInHand(self, PsychicFrog))
            score += 0.5;

        return score;
    }

    // ── TryGetNextWinningAction ─────────────────────────────────────────────

    /// <summary>
    /// Advisory-only — always returns <see langword="null"/>.
    ///
    /// <para>Reanimation is a 2-turn ENGINE (discard Archon → reanimate Emperor
    /// → adapt → Archon returns), NOT an atomic kill. Directive override of the
    /// MCTS search on a multi-turn engine over-commits the bot to the combo
    /// line regardless of board state, which loses: a controlled measurement
    /// showed 20 % win-rate with directive enabled (combo fired 12/16 games)
    /// vs the no-strategy baseline.</para>
    ///
    /// <para><see cref="StrategicScore"/> steers the search toward assembling
    /// and executing the engine; the search evaluates whether each line pays off
    /// and decides timing autonomously. Directive override is reserved for
    /// ATOMIC, (near-)immediate kills — see
    /// <see cref="IDeckStrategy.TryGetNextWinningAction"/>.</para>
    /// </summary>
    public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self)
    {
        // Reanimation is a multi-turn engine, not an atomic kill.
        // StrategicScore steers the MCTS search toward assembling + executing
        // the plan; the search sees whether the line pays off and decides
        // timing. Directive override hurts here (measured: over-commits, loses).
        return null;
    }

    // ── AdviseMulligan ──────────────────────────────────────────────────────

    /// <summary>
    /// Keep hands that have at least one land AND at least one functional
    /// piece: a graveyard-fill enabler (Faithless Looting, Thought Scour,
    /// Psychic Frog) or a reanimation spell (Persist, Unearth).
    ///
    /// Ship hands with no lands (can't cast anything) or no functional
    /// pieces (a hand of seven Archons / Swamps does nothing on turns 1–3).
    /// Returns null once ≥ 3 mulligans have been taken (defer to generic
    /// policy — keep anything at that depth).
    /// </summary>
    public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
    {
        // Defer to generic policy at high-mulligan depth.
        if (mulligansTaken >= 3) return null;

        var hasLand = hand.Any(c => c is Land);
        if (!hasLand) return MulliganDecision.Mulligan;

        var hasEnabler = hand.Any(c =>
            c.Name == FaithlessLooting
            || c.Name == ThoughtScour
            || c.Name == PsychicFrog
            || c.Name == PersistSpell
            || c.Name == UnearthSpell);

        if (!hasEnabler) return MulliganDecision.Mulligan;

        return MulliganDecision.Keep;
    }
}
