using Majik.Bot.Decks;
using Majik.Bot.Search;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Strategies;

/// <summary>
/// Goblin Charbelcher combo executor — the bot-plays-the-combo deliverable
/// (plan 2026-06-13, Phase C). Registered for the WU <c>AzoriusLotusBelcher</c>
/// archetype and the red <c>Belcher</c> archetype: both win the SAME way —
/// Goblin Charbelcher onto the battlefield, then <c>{3},{T}</c> belch a
/// (near-)landless library at the opponent for lethal damage — so one solver
/// drives both keys.
///
/// <para>
/// <b>A mana-arithmetic line SOLVER, not a fixed script.</b> Each priority
/// window <see cref="TryGetNextWinningAction"/> RE-DERIVES the next concrete
/// action from the CURRENT board (idempotent state machine over board
/// conditions). The line that gets walked varies (Charbelcher already in play
/// vs hard-cast from hand; mana floated from Lotus Bloom vs lands) — the solver
/// recomputes detection every call and advances one step.
/// </para>
///
/// <para>
/// <b>Why the previous (non-firing) BelcherStrategy could not pilot the WU
/// deck.</b> The live priority loop pays an ACTIVATED ability's mana cost from
/// the FLOATING POOL ONLY (<c>ManaCostCost.CanPay → ManaPool.CanPay</c>;
/// <see cref="Majik.Core.Game.TurnDriver"/> <c>DispatchActivate</c> swallows a
/// proposal whose mana was never floated). The old strategy returned the belch
/// whenever <see cref="LegalActionEnumerator.UntappedManaSources"/> ≥ 3 — true
/// even when the {3} sits UNTAPPED in Lotus Bloom / lands, never floated — so
/// the dispatch silently dropped it and the combo never fired. The red ritual
/// deck happened to work because rituals put mana INTO the floating pool; the WU
/// deck's mana lives in tappable permanents. This solver FLOATS the {3} first
/// (emitting <see cref="PriorityAction.ActivateManaAbility"/>, which the loop
/// treats as implicit hold-priority, CR 605.3a) and only then fires the belch.
/// </para>
///
/// <para>
/// <b>Whir of Invention is deliberately NOT a path.</b> Whir is {X}{U}{U}{U} —
/// a variable-X / improvise cast the autonomous priority loop's cast dispatch
/// cannot drive (documented deferred gap, Phase B
/// <c>BelcherManaLineTests.WhirOfInvention_VariableXCast_OnAutonomousLoop_IsRejected_DeferredGap</c>).
/// The solver therefore only assembles Charbelcher via already-in-play or a
/// {4} hard-cast from hand.
/// </para>
/// </summary>
[DeckStrategy("AzoriusLotusBelcher")]
[DeckStrategy("Belcher")]
internal sealed class BelcherComboSolver : IDeckStrategy
{
    // ── Key card names ──────────────────────────────────────────────────────

    /// The win condition. {4} to cast; {3},{T} to activate the belch.
    private const string GoblinCharbelcher = "Goblin Charbelcher";

    /// <summary>{3} activation cost of the Charbelcher belch.</summary>
    private const int ActivationMana = 3;

    /// <summary>{4} hard-cast cost of Goblin Charbelcher.</summary>
    private const int HardCastMana = 4;

    // ── IDeckStrategy.ReferencedCardNames ───────────────────────────────────

    /// <summary>
    /// Card names this solver references. Validated at test time against the
    /// archetype's <see cref="BotDeckCatalog.Get"/> list — Goblin Charbelcher is
    /// in BOTH the AzoriusLotusBelcher and Belcher lists, so a single name keeps
    /// the coverage tripwire green for both registered keys.
    /// </summary>
    public IReadOnlyList<string> ReferencedCardNames { get; } = new[]
    {
        GoblinCharbelcher,
    };

    // ── StrategicScore ──────────────────────────────────────────────────────

    /// <summary>
    /// Steer the search toward assembling the combo: Charbelcher on board is the
    /// most-assembled state, Charbelcher in hand is one cast away. (The solver's
    /// directive does the actual firing; this only nudges the search while the
    /// pieces are still being gathered.)
    /// </summary>
    public double StrategicScore(GameContext ctx, Player self)
    {
        double score = 0;
        if (DeckStrategyHelpers.HasOnBoard(self, GoblinCharbelcher)) score += 5.0;
        if (DeckStrategyHelpers.HasInHand(self, GoblinCharbelcher)) score += 2.0;
        return score;
    }

    // ── TryGetNextWinningAction — the line solver ───────────────────────────

    /// <summary>
    /// Re-derive the next concrete step toward the Charbelcher kill from the
    /// CURRENT board, or null if the kill is not assemblable this turn (the bot
    /// then plays normally — develops mana / pieces).
    ///
    /// <para>Detection (C1): the kill is assemblable iff
    /// <c>UntappedManaSources(self) ≥ pathCost + {3}</c>, Charbelcher is
    /// reachable (on board → pathCost 0, or in hand → hard-cast pathCost {4}),
    /// AND the belch is lethal (library nonland count ≥ opponent life).</para>
    ///
    /// <para>Walk (C2), in priority order on the current board:</para>
    /// <list type="number">
    ///   <item>Charbelcher on board + {3} FLOATING → fire the belch at the
    ///     opponent (lethal).</item>
    ///   <item>Charbelcher on board + {3} not yet floating → float toward {3}
    ///     by activating a mana source (Lotus Bloom first — one tap yields the
    ///     whole {3}; else any tappable source). Implicit hold-priority advances
    ///     the line; the next window re-derives and either floats more or fires.</item>
    ///   <item>Charbelcher in hand + assemblable → hard-cast it ({4}); the
    ///     engine auto-taps lands. Next window it is on board → arm 1/2.</item>
    /// </list>
    /// </summary>
    public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self)
    {
        // Need an opponent to belch at. (Use the explicit `self`, which may
        // differ from ctx.Self when the directive runs for a sandbox seat.)
        var opponent = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, self));
        if (opponent is null) return null;

        bool charbelcherOnBoard = DeckStrategyHelpers.HasOnBoard(self, GoblinCharbelcher);
        bool charbelcherInHand = DeckStrategyHelpers.HasInHand(self, GoblinCharbelcher);

        // ── Detection (C1) ──────────────────────────────────────────────────
        if (!IsKillAssemblable(self, opponent, charbelcherOnBoard, charbelcherInHand))
            return null;

        int floating = self.ManaPool.Total;

        // ── Arm 1/2: Charbelcher already on board ───────────────────────────
        if (charbelcherOnBoard)
        {
            // Arm 1 — the {3} is FLOATING (the live dispatch pays the activated
            // ability from the pool only): fire the belch now.
            if (floating >= ActivationMana)
            {
                var belch = DeckStrategyHelpers.BuildActivate(
                    ctx, self, GoblinCharbelcher, target: opponent);
                if (belch is not null) return belch;
                // BuildActivate gates on the belch being untapped + affordable
                // (UntappedManaSources). If it can't build (Charbelcher already
                // tapped, e.g. a prior belch this turn), there is no line.
                return null;
            }

            // Arm 2 — float toward the {3}. The belch can't be paid until the
            // mana is in the pool, so tap a source now and re-derive next window.
            var floatStep = BuildFloatStep(self);
            if (floatStep is not null) return floatStep;

            // No floatable source and not enough floating — can't fire. (Should
            // not happen: detection already proved UntappedManaSources ≥ {3}.)
            return null;
        }

        // ── Arm 3: Charbelcher in hand → hard-cast ({4}) ────────────────────
        // Detection proved UntappedManaSources ≥ {4}+{3}; cast it (engine
        // auto-taps lands). BuildCast applies the sorcery-window + affordability
        // gates and returns null when the cast isn't legal right now.
        if (charbelcherInHand)
        {
            var cast = DeckStrategyHelpers.BuildCast(ctx, self, GoblinCharbelcher);
            if (cast is not null) return cast;
        }

        return null;
    }

    // ── Detection helper (C1) ───────────────────────────────────────────────

    /// <summary>
    /// The kill is assemblable THIS turn iff: Charbelcher is reachable (on board
    /// or in hand), the total mana available (floating + untapped tappable
    /// sources — the auto-tap model) covers the cheapest path to Charbelcher in
    /// play PLUS the {3} activation, and the belch would be lethal (the library
    /// has at least as many nonland cards as the opponent has life — a landless
    /// reveal deals nonland-count damage, post-fix Charbelcher).
    /// </summary>
    private static bool IsKillAssemblable(
        Player self, Player opponent, bool charbelcherOnBoard, bool charbelcherInHand)
    {
        if (!charbelcherOnBoard && !charbelcherInHand) return false;

        int pathCost = charbelcherOnBoard ? 0 : HardCastMana;
        int needed = pathCost + ActivationMana;

        int available = LegalActionEnumerator.UntappedManaSources(self);
        if (available < needed) return false;

        // Lethality: a reveal-until-LAND belch deals damage = nonland cards
        // revealed. Count the library's nonland cards (MDFC fronts are nonland
        // by CR 712.4a, so the WU manabase reveals as nonland). Lethal iff that
        // count ≥ the opponent's current life.
        int nonlandInLibrary = self.Zones.Library.GetCards()
            .Count(c => !c.HasType(CardType.Land));

        return nonlandInLibrary >= opponent.LifeTotal;
    }

    // ── Float helper (C2 arm 2) ─────────────────────────────────────────────

    /// <summary>
    /// Build the next mana-ability activation to float mana toward the {3} belch
    /// cost. Prefers Lotus Bloom (its "{T}, Sacrifice: add three of one color"
    /// floats the whole {3} in one tap, leaving lands for other uses); otherwise
    /// taps the first untapped mana source. Returns null when no untapped source
    /// with an activatable mana ability is available.
    /// </summary>
    private static PriorityAction? BuildFloatStep(Player self)
    {
        // First pass: Lotus Bloom — one tap yields {3}, the entire cost.
        var bloom = FindFloatableSource(self, "Lotus Bloom");
        if (bloom is not null) return bloom;

        // Otherwise: any untapped permanent with an activatable mana ability.
        foreach (var card in self.Zones.Battlefield.GetCards())
        {
            var step = TryBuildFloat(card);
            if (step is not null) return step;
        }

        return null;
    }

    /// <summary>Build a float step for a specifically-named source, or null.</summary>
    private static PriorityAction? FindFloatableSource(Player self, string name)
    {
        var card = self.Zones.Battlefield.GetCards().FirstOrDefault(c => c.Name == name);
        return card is null ? null : TryBuildFloat(card);
    }

    /// <summary>
    /// Build an <see cref="PriorityAction.ActivateManaAbility"/> for the first
    /// activatable non-zero mana ability on <paramref name="card"/>, or null if
    /// the card is tapped / has no activatable mana ability.
    /// </summary>
    private static PriorityAction? TryBuildFloat(ICard card)
    {
        if (card is Permanent perm && perm.IsTapped) return null;

        var mana = card.Abilities.OfType<IManaAbility>().FirstOrDefault(a => a.CanActivate());
        return mana is null ? null : new PriorityAction.ActivateManaAbility(card, mana);
    }

    // ── AdviseMulligan ──────────────────────────────────────────────────────

    /// <summary>
    /// Keep hands holding Goblin Charbelcher or a Lotus Bloom (the payoff or the
    /// suspend ramp toward it). The WU deck runs no true lands — its mana is MDFC
    /// backs + Lotus Bloom — so the keep rule mirrors the deck's real need: the
    /// payoff in hand, or an early acceleration piece. Defer to the generic
    /// policy after ≥ 3 mulligans.
    /// </summary>
    public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int mulligansTaken)
    {
        if (mulligansTaken >= 3) return null;

        if (hand.Any(c => c.Name == GoblinCharbelcher)) return MulliganDecision.Keep;
        if (hand.Any(c => c.Name == "Lotus Bloom")) return MulliganDecision.Keep;

        return MulliganDecision.Mulligan;
    }
}
