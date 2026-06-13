using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Search;

/// <summary>
/// Enumerates all legal priority actions for a player at a given decision
/// point. Pure enumeration — no scoring. Single source of truth shared by
/// <see cref="Majik.Bot.Heuristic.PriorityPolicy"/> (which scores these) and
/// the bot search (Task B1 / spec §10.3).
///
/// <para>
/// Sorcery-speed gating follows CR 116.2a (active player, main phase, empty
/// stack). Land drops follow CR 305.2 (one land per turn — the engine enforces
/// the hard rule; this enumerator offers the action whenever a land is in hand
/// and we are in a sorcery window, mirroring <see cref="Majik.Bot.Heuristic.PriorityPolicy"/>).
/// Activated abilities follow CR 602.
/// </para>
/// </summary>
internal static class LegalActionEnumerator
{
    /// <summary>
    /// All legal priority actions for <paramref name="self"/> at this decision,
    /// INCLUDING Pass. Pure enumeration — no scoring. Single source of truth
    /// shared by PriorityPolicy (which scores these) and the bot search.
    /// </summary>
    public static IReadOnlyList<PriorityAction> ForPriority(GameContext ctx, Player self)
    {
        var result = new List<PriorityAction>();

        // Pass is always legal (CR 117.3).
        result.Add(PriorityAction.Pass);

        // CR 116.2a sorcery window: active player, main phase, empty stack.
        var sorceryWindow = ctx.ActivePlayer == self
            && ctx.CurrentPhase is { } phase && phase.IsMain()
            && ctx.Stack.Count == 0;

        // CR 305.2 — land play (sorcery speed). Use ctx.LandPlayAvailable, which
        // is the authoritative per-turn-cap + phase + turn gate computed by the
        // priority loop's LandDropTracker. This prevents the MCTS priority search
        // from offering PlayLand on the opponent's turn, outside main, or after
        // the land drop is already used — all of which the engine rejects, causing
        // a spin loop (54k+ rejected PlayLand calls in Phase 2A measurement).
        if (ctx.LandPlayAvailable)
        {
            var landInHand = self.Zones.Hand.GetCards().OfType<Land>().FirstOrDefault();
            if (landInHand != null)
                result.Add(new PriorityAction.PlayLand(landInHand));

            // CR 305.2 — Harnfel, Horn of Bounty: "you may play those cards this
            // turn." A LAND in our exile pile carries a runtime land-play grant
            // and is PLAYED, not cast (CR 601.1) — surface it as a land drop so
            // the search bot isn't blind to a free land sitting in exile. Played
            // from Exile; still consumes the CR 305.2 land drop via the loop's
            // LandDropTracker.
            var exileLand = Majik.Core.Keywords.ExilePlayPermission
                .PlayableLandsFromExile(self).FirstOrDefault();
            if (landInHand == null && exileLand != null)
                result.Add(new PriorityAction.PlayLand(exileLand));

            // CR 305 / 712.3 — an MDFC back-face LAND play is a land play, not a
            // spell: surface it whenever the land drop is available, regardless
            // of front-face affordability (the engine's CastSpell dispatch raises
            // the face prompt; MdfcFacePolicy picks the land face). Without this
            // arm a 0-land MDFC hand is permanently mana-locked: the CastSpell arm
            // below gates on front-face affordability, so at 0 mana the face-choice
            // point is never reached (Belcher trace, 2026-06-12).
            foreach (var card in self.Zones.Hand.GetCards())
            {
                if (card is Card c
                    && c.MdfcState is { CanCastEitherFace: true, CastableBackFace: { IsLand: true } })
                {
                    result.Add(new PriorityAction.CastSpell(c, Array.Empty<object>()));
                }
            }
        }

        // Spell casts: any player with priority may cast a spell when timing
        // permits (CR 601.1). Instant-speed spells (Instants, Flash permanents)
        // may be cast on any player's turn. Sorcery-speed spells require the
        // caster to be the active player AND the stack to be empty (CR 116.2a).
        {
            var manaAvailable = UntappedManaSources(self);
            foreach (var card in self.Zones.Hand.GetCards())
            {
                if (card is Land) continue;

                var instantSpeed = IsInstantSpeed(card);
                // Instant-speed: legal regardless of active player.
                // Sorcery-speed: only legal in a sorcery window (active player
                //   + main phase + empty stack — checked above).
                if (!instantSpeed && !sorceryWindow) continue;

                if (ApproxCmc(card) <= manaAvailable)
                    result.Add(new PriorityAction.CastSpell(card, Array.Empty<object>()));
            }

            // CR 118.9 — cast-from-exile runtime grants (madness / Ragavan /
            // foretell / impulse). A card in our EXILE zone carrying a
            // RuntimeExileCast grant that nominates US is a legal cast at the
            // granted cost via ExileCastAlternativeCost. The live priority loop
            // surfaces these for human / remote agents; without this loop the
            // search bot is blind to them. Mirror the hand-cast gates: instant
            // vs. sorcery speed and colour-blind affordability.
            foreach (var card in self.Zones.Exile.GetCards())
            {
                if (card is not Card c) continue;
                if (!ReferenceEquals(c.RuntimeExileCastAllowedCaster, self)) continue;
                if (c.RuntimeExileCastCost is not { } exileCost) continue;
                if (c is Land) continue;

                var instantSpeed = IsInstantSpeed(c);
                if (!instantSpeed && !sorceryWindow) continue;

                if (exileCost.TotalValue > manaAvailable) continue;

                result.Add(new PriorityAction.CastSpell(
                    card,
                    Array.Empty<object>(),
                    AlternativeCost: new Majik.Core.Costs.ExileCastAlternativeCost(
                        $"Cast {c.Name} from exile ({exileCost})", exileCost)));
            }
        }

        // CR 602 — activated abilities of permanents the player controls.
        // Mana abilities are excluded (fired as part of mana payment, not here).
        // Affordability is symmetric with casting (CR 116.2a / 602.2):
        //   - Mana costs are checked against UntappedManaSources (floating + untapped
        //     tappable sources), so the bot enumerates activations the engine can pay
        //     by auto-tapping — not only when the mana is already floating.
        //   - Non-mana costs (tap, sacrifice, pay-life, etc.) continue to use
        //     cost.CanPay(self) which checks the actual game-state condition.
        {
            var manaAvailable = UntappedManaSources(self);
            foreach (var card in self.Zones.Battlefield.GetCards())
            {
                foreach (var ability in card.Abilities.OfType<IActivatedAbility>())
                {
                    if (ability is IManaAbility) continue;
                    // CR 602.5c — respect the "Activate only if <condition>" gate.
                    // Prefer the context-aware overload (ctx is in scope) so a gate
                    // that reads live game state — Hired Claw's "an opponent lost life
                    // this turn" — is honoured here, matching the live driver. A
                    // context-less gate still evaluates via the same overload's
                    // fallback.
                    var canActivate = ability is Majik.Core.Abilities.ActivatedAbility aa
                        ? aa.CanActivateNow(ctx)
                        : ability.CanActivateNow();
                    if (!canActivate) continue;
                    // Affordability is symmetric with casting: mana portion against
                    // UntappedManaSources (floating + tappable), non-mana costs via CanPay.
                    if (CanAffordAbility(ability, self, manaAvailable))
                        result.Add(new PriorityAction.ActivateAbility(ability, Array.Empty<object>()));
                }
            }
        }

        // CR 606.3 — loyalty abilities of planeswalkers the player controls.
        // Sorcery-speed only (active player + main phase + empty stack — the
        // same sorceryWindow predicate the land/sorcery-cast paths use), plus
        // the once-per-turn + sufficient-loyalty gate (CR 606.3/606.5).
        // LoyaltyAbility is its own ability shape (not IActivatedAbility); the
        // dispatcher builds the stack object from it on activation.
        if (sorceryWindow)
        {
            foreach (var card in self.Zones.Battlefield.GetCards())
            {
                foreach (var loyalty in card.Abilities.OfType<LoyaltyAbility>())
                {
                    if (loyalty.CanActivate())
                        result.Add(new PriorityAction.ActivateLoyaltyAbility(loyalty, Array.Empty<object>()));
                }
            }
        }

        return result;
    }

    // ── Helpers (same semantics as PriorityPolicy's private equivalents) ────────

    /// <summary>
    /// Affordability check for an activated ability — symmetric with spell
    /// casting (CR 602.2 / 116.2a).
    ///
    /// <para>
    /// The mana portion of the cost is checked against
    /// <paramref name="manaAvailable"/> (= <see cref="UntappedManaSources"/>),
    /// which includes both floating mana and untapped tappable sources.  This
    /// matches the approximation used for spell casting and mirrors how
    /// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> auto-taps sources to
    /// pay mana costs at resolution.
    /// </para>
    ///
    /// <para>
    /// Non-mana costs (e.g. <c>{T}</c>, sacrifice, pay-life) keep their
    /// existing <see cref="ICost.CanPay"/> check, which evaluates the actual
    /// game-state condition (untapped + not summoning-sick for tap costs,
    /// controller match for sacrifice, sufficient life for pay-life).
    /// </para>
    ///
    /// <para>
    /// Like <see cref="ApproxCmc"/>, this is colour-blind — it does not
    /// verify that the available mana can satisfy the colour requirement.
    /// The anti-spin memos and the engine's action-validator handle the rare
    /// case where a colour mismatch prevents the cost from actually being paid
    /// at resolution.
    /// </para>
    /// </summary>
    internal static bool CanAffordAbility(IActivatedAbility ability, Player self, int manaAvailable)
    {
        foreach (var cost in ability.Costs)
        {
            if (cost is Majik.Core.Costs.ManaCostCost manaCostCost)
            {
                // Mana portion: affordable by tapping — same model as casting.
                if (manaCostCost.Cost.TotalValue > manaAvailable)
                    return false;
            }
            else if (!cost.CanPay(self))
            {
                // Non-mana cost: tap, sacrifice, pay-life, etc.
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Converted mana cost via the engine's parser. Matches the convention
    /// used by <see cref="Majik.Bot.Heuristic.PriorityPolicy"/>.
    /// </summary>
    internal static int ApproxCmc(ICard card)
        => ManaCost.Parse(card.ManaCost ?? string.Empty).TotalValue;

    /// <summary>
    /// Total mana available to <paramref name="self"/> RIGHT NOW — the
    /// correct affordability gate for enumeration-time cast decisions.
    ///
    /// <para>Three components (all colour-blind approximations, consistent
    /// with the existing <see cref="ApproxCmc"/> colour-blind check):</para>
    /// <list type="number">
    ///   <item>
    ///     <description><b>Floating pool</b> — <c>ManaPool.Total</c>:
    ///     mana already produced by rituals, dorks activated earlier,
    ///     etc. Available immediately with no further action.</description>
    ///   </item>
    ///   <item>
    ///     <description><b>Untapped mana-source permanents</b> — every
    ///     untapped permanent whose <see cref="IManaAbility"/> is currently
    ///     activatable (i.e. <c>CanActivate()</c> returns true, which already
    ///     gates summoning-sickness for creatures via
    ///     <c>SummoningSicknessTapGate</c>). This subsumes the old
    ///     lands-only count and correctly adds mana dorks, mana rocks, and
    ///     Treasure tokens. The mana counted per source is
    ///     <c>ability.ManaGenerated.Total</c> clamped to ≥ 1; dynamic
    ///     generators report <see cref="ManaCost.Zero"/> until activated, so
    ///     the clamp ensures we never under-count them.</description>
    ///   </item>
    /// </list>
    ///
    /// <para>Matches the discovery logic in
    /// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> (lines 109–122),
    /// which also iterates untapped permanents and calls
    /// <see cref="IManaAbility.CanActivate"/>. This method is called without
    /// a <c>ContinuousEffectsService</c>, so Blood-Moon / Spreading-Seas
    /// subtype rewrites are ignored here; that is an acceptable approximation
    /// for enumeration speed.</para>
    /// </summary>
    internal static int UntappedManaSources(Player self)
    {
        // 1. Floating mana — already available.
        int total = self.ManaPool.Total;

        // 2. Untapped permanents with an activatable mana ability.
        //    Mirrors ManaPaymentResolver's candidate scan (lines 109–122):
        //    iterate battlefield, skip tapped, check IManaAbility.CanActivate().
        //    CanActivate() internally calls SummoningSicknessTapGate so
        //    summoning-sick creatures correctly return false here.
        foreach (var card in self.Zones.Battlefield.GetCards())
        {
            if (card is not Permanent perm) continue;
            if (perm.IsTapped) continue;

            // Use printed abilities directly (no layer service at enum-time —
            // acceptable approximation; mirrors EffectiveManaAbilities.For
            // with layers=null which falls back to the same list).
            var manaAbilities = perm.Abilities.OfType<IManaAbility>().ToList();

            if (manaAbilities.Count == 0)
            {
                // Fallback for bare Land instances (e.g. in unit tests) that
                // have no explicit mana ability attached: any Land can in
                // principle produce 1 mana. Non-land permanents without a mana
                // ability produce nothing and are skipped.
                if (perm is Land)
                    total += 1;
                continue;
            }

            foreach (var ability in manaAbilities)
            {
                if (!ability.CanActivate()) continue;

                // ManaGenerated.TotalValue is 0 for dynamic generators (Cabal
                // Coffers, etc.) until actually activated. Clamp to 1 so
                // every activatable source contributes at least 1 mana.
                total += Math.Max(1, ability.ManaGenerated.TotalValue);
            }
        }

        return total;
    }

    /// <summary>
    /// Instant-speed cast eligibility — Instants, and Flash permanents
    /// (CR 702.8). Mirrors <see cref="Majik.Bot.Heuristic.PriorityPolicy"/>.
    /// </summary>
    internal static bool IsInstantSpeed(ICard c)
    {
        if (c.HasType(CardType.Instant)) return true;
        return c.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
    }
}
