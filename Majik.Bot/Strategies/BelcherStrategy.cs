using Majik.Bot.Decks;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Strategies;

/// <summary>
/// Deck strategy for Belcher (Modern Boros-touched ritual combo).
///
/// <para>
/// Primer: chain mana rituals (Desperate Ritual, Pyretic Ritual, Rite of
/// Flame-analogue via Irencrag Feat / Strike It Rich / Manamorphose) into a
/// single Goblin Charbelcher ({4}) cast, then activate it ({3}, {T}) for a
/// near-lethal belch. The Belcher deck runs zero or near-zero lands so the
/// belch reveals ~50 cards before hitting a nonland — dealing ~50 damage in
/// one activation. This is the textbook ATOMIC kill: one activation = game
/// over on the spot.
/// </para>
///
/// <para>
/// <b>Why DIRECTIVE is correct here:</b> unlike multi-turn engines
/// (GrixisReanimator where directive over-commits and loses), the Charbelcher
/// activation is a single triggered resolution that immediately ends the game.
/// The MCTS search cannot see this line because the eval function does not
/// model "activate this artifact = deal 50 to face". DIRECTIVE is the only
/// mechanism that can find and fire this kill. <see cref="TryGetNextWinningAction"/>
/// therefore returns the Charbelcher activation whenever it is executable.
/// </para>
///
/// <para>
/// <see cref="StrategicScore"/> steers the search toward the combo line:
/// Charbelcher on board (payoff assembled, high bonus), Charbelcher in hand
/// + mana headroom (one cast away, moderate bonus), rituals in hand
/// (setup progressing, low bonus).
/// </para>
/// </summary>
[DeckStrategy("Belcher")]
internal sealed class BelcherStrategy : IDeckStrategy
{
    // ── Key card names ──────────────────────────────────────────────────────

    // The win condition.  Costs {4} to cast; {3}, {T} to activate.
    private const string GoblinCharbelcher = "Goblin Charbelcher";

    // Mana rituals — net-positive or net-even mana producers that build toward
    // the {4} cast + {3} activation.  All are in the Belcher deck list.
    private const string DesperateRitual = "Desperate Ritual";
    private const string PyreticRitual   = "Pyretic Ritual";
    private const string Manamorphose    = "Manamorphose";
    private const string IrencragFeat    = "Irencrag Feat";
    private const string StrikeItRich    = "Strike It Rich";

    // ── IDeckStrategy.ReferencedCardNames ───────────────────────────────────

    /// <summary>
    /// All card names referenced in strategy logic.  Validated at test time
    /// against <see cref="BotDeckCatalog.Get"/>("Belcher") so every name must
    /// be in the deck list.
    /// </summary>
    public IReadOnlyList<string> ReferencedCardNames { get; } = new[]
    {
        GoblinCharbelcher,
        DesperateRitual,
        PyreticRitual,
        Manamorphose,
        IrencragFeat,
        StrikeItRich,
    };

    // ── StrategicScore ──────────────────────────────────────────────────────

    /// <summary>
    /// Scores board progress toward the Belcher combo:
    ///
    /// <list type="bullet">
    ///   <item>+5.0 — Goblin Charbelcher is on the battlefield (payoff
    ///     assembled; one activation = game over).</item>
    ///   <item>+2.0 — Goblin Charbelcher is in hand (one cast away from the
    ///     win condition being assembled).</item>
    ///   <item>+0.5 per ritual in hand — each ritual progresses toward the
    ///     mana needed for the cast + activation (Desperate Ritual, Pyretic
    ///     Ritual, Manamorphose, Irencrag Feat, Strike It Rich).</item>
    /// </list>
    ///
    /// These bonuses fold into MCTS BoardEval at rollout leaves to steer the
    /// search toward assembling and firing the Charbelcher rather than taking
    /// random creature-beats lines.
    /// </summary>
    public double StrategicScore(GameContext ctx, Player self)
    {
        double score = 0;

        // Payoff already on board — one activation away from the kill.
        if (DeckStrategyHelpers.HasOnBoard(self, GoblinCharbelcher))
            score += 5.0;

        // Payoff in hand — one cast-then-activate cycle away.
        if (DeckStrategyHelpers.HasInHand(self, GoblinCharbelcher))
            score += 2.0;

        // Count rituals in hand — each one progresses toward sufficient mana.
        var hand = DeckStrategyHelpers.Hand(self);
        foreach (var card in hand)
        {
            if (card.Name is DesperateRitual or PyreticRitual
                or Manamorphose or IrencragFeat or StrikeItRich)
            {
                score += 0.5;
            }
        }

        return score;
    }

    // ── Mana thresholds ─────────────────────────────────────────────────────

    /// <summary>
    /// Minimum mana in the floating pool required to cast Goblin Charbelcher
    /// ({4}) AND immediately activate it ({3},{T}) in the same priority window
    /// after resolution.  The full line costs 4 + 3 = 7 total mana, all of
    /// which must be available as floating mana (the Belcher deck runs 0 lands,
    /// so there is no tap-to-pay source available after rituals burn their
    /// mana).
    ///
    /// <para>
    /// Rationale: if the directive allowed casting Charbelcher whenever the
    /// pool >= 4 (the bare cast cost), the pool would be 0 after the cast and
    /// the activation would be impossible the same turn.  The heuristic fallback
    /// also scores the cast positively at pool = 4, so without this threshold
    /// the bot always casts Charbelcher sub-optimally and then stalls (board
    /// shows Charbelcher but 0 mana → 0/12 belch activations observed in the
    /// 12-game pilot run).
    /// </para>
    /// </summary>
    private const int MinManaForFullLine = 7; // {4} cast + {3} activation

    // ── TryGetNextWinningAction ─────────────────────────────────────────────

    /// <summary>
    /// DIRECTIVE atomic-kill override — two arms:
    ///
    /// <list type="number">
    ///   <item>
    ///     <b>Activate arm</b> — Charbelcher is already on the battlefield,
    ///     untapped, and the floating pool contains ≥ {3}.  Returns the
    ///     activation immediately; the belch deals ~50 damage in a single
    ///     resolution and ends the game on the spot.
    ///   </item>
    ///   <item>
    ///     <b>Cast arm</b> — Charbelcher is in hand and the floating pool
    ///     contains ≥ {7} ({4} for the cast + {3} for the immediate
    ///     activation).  Directs a sorcery-speed CastSpell so that after
    ///     resolution the pool still has ≥ {3} floating and the activate arm
    ///     fires on the next priority call.
    ///     The cast arm short-circuits the heuristic's sub-optimal path of
    ///     casting at pool = 4 (which drains the pool and strands Charbelcher
    ///     on the board with no activation mana).
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// If neither arm fires (Charbelcher not assembled, or pool insufficient),
    /// returns null and defers to the search / heuristic.
    /// </para>
    /// </summary>
    public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self)
    {
        // Require an opponent to target.
        var opponent = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, self));
        if (opponent is null) return null;

        // ── Arm 1: activate — Charbelcher on board with {3} floating ─────────
        // BuildActivate null-guards: returns null if the permanent is absent
        // OR if any of its costs (ManaCostCost("{3}") + AdditionalCost.Tap)
        // cannot be paid — exactly the correct gate.
        var activate = DeckStrategyHelpers.BuildActivate(ctx, self, GoblinCharbelcher, target: opponent);
        if (activate is not null) return activate;

        // ── Arm 2: cast — Charbelcher in hand, pool ≥ 7 (full line affordable)
        // Only fire when the floating pool already covers the cast ({4}) AND
        // the subsequent activation ({3}).  ManaCostCost.CanPay checks the
        // floating pool only (not tappable sources), which is correct here:
        // the Belcher deck has no tappable mana sources after rituals resolve
        // (Talisman / Treasure are either already tapped or absent), so the
        // full 7 must be floating.
        //
        // Sorcery-window guard mirrors DeckStrategyHelpers.BuildCast: require
        // active player + main phase + empty stack (CR 116.2a).  The card is
        // a sorcery-speed permanent ({4} artifact), so instant-speed bypass
        // is not available.
        if (DeckStrategyHelpers.HasInHand(self, GoblinCharbelcher)
            && self.ManaPool.Total >= MinManaForFullLine)
        {
            // BuildCast applies the affordability gate (ApproxCmc({4}) = 4 ≤
            // UntappedManaSources) and the sorcery-window gate. If either fails
            // (e.g. wrong phase, stack non-empty, or timing mismatch) it returns
            // null and we fall through to the heuristic.
            var cast = DeckStrategyHelpers.BuildCast(ctx, self, GoblinCharbelcher);
            if (cast is not null) return cast;
        }

        return null;
    }

    // ── AdviseMulligan ──────────────────────────────────────────────────────

    /// <summary>
    /// Keep hands that contain Goblin Charbelcher OR at least two rituals
    /// (enough mana engine to dig toward the win).  Ship hands with neither.
    ///
    /// <para>
    /// The Belcher deck runs no lands, so the classic "keep if you have a
    /// land" rule is irrelevant.  Instead the keep criteria mirrors the
    /// deck's actual requirement: the payoff itself OR enough mana producers
    /// to cast it (you need {4} for the cast then {3} for the activation).
    /// Two rituals each netting at least 1 red covers the low-end functional
    /// floor; the search will find lines from there.
    /// </para>
    ///
    /// <para>
    /// Returns null once ≥ 3 mulligans have been taken — defer to generic
    /// policy at that depth (keep anything playable).
    /// </para>
    /// </summary>
    public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
    {
        // Defer to generic policy at high-mulligan depth.
        if (mulligansTaken >= 3) return null;

        // Keep if Charbelcher is already in hand — the win condition is there.
        if (hand.Any(c => c.Name == GoblinCharbelcher))
            return MulliganDecision.Keep;

        // Keep if at least two rituals are in hand — enough mana engine to
        // assemble the line.
        int ritualCount = hand.Count(c =>
            c.Name is DesperateRitual or PyreticRitual
            or Manamorphose or IrencragFeat or StrikeItRich);

        if (ritualCount >= 2)
            return MulliganDecision.Keep;

        // No Charbelcher and fewer than two rituals — this hand can't combo.
        return MulliganDecision.Mulligan;
    }
}
