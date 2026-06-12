using Majik.Core.Abilities;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.StateMachine;

namespace Majik.Core.Api;

/// <summary>
/// Narrows the priority-prompt command kinds to those that are at least
/// plausibly legal at this priority moment. Shared between
/// <see cref="RemoteAgent"/> (which surfaces the kinds to the wire
/// PromptDto so the portal can render the right UI) and
/// <see cref="Majik.Core.Game.PriorityLoop"/> (which consults the same
/// computation for its server-side auto-pass gate — Slice 5a).
///
/// <para>Holding the single source of truth here means the engine's
/// "is this a dead window?" test can't drift from the portal's
/// pass-only detection.</para>
///
/// <para>Conservative by design — false positives (offering a kind the
/// player can't actually use) are acceptable because the engine still
/// validates each submitted command; false negatives (hiding a kind
/// the player legitimately can use) would lock the user out and are
/// catastrophic. The bot path (BotPlayerAgent / Heuristic /
/// Deterministic) enumerates its own moves and does not consult these
/// kinds, so this narrowing is purely a UX hint for remote (human)
/// clients AND the engine's auto-pass detector.</para>
/// </summary>
public static class PriorityKinds
{
    /// <summary>
    /// Compute the legal command kinds at the given priority moment.
    /// <see cref="PassPriorityCommand"/> is always present (CR 117.4 —
    /// passing priority is a player's fundamental action).
    /// </summary>
    public static Type[] Build(GameContext ctx)
    {
        // PassPriorityCommand is always legal — passing priority is a
        // player's fundamental action at every priority window (CR 117.4).
        var kinds = new List<Type>(3) { typeof(PassPriorityCommand) };

        var hand = ctx.Self.Zones.Hand.GetCards();
        var sorceryWindow = ctx.CurrentPhase is { } sorceryPhase && sorceryPhase.IsMain()
            && ReferenceEquals(ctx.Self, ctx.ActivePlayer)
            && ctx.Stack.IsEmpty;

        // CR 305.2 — lands are sorcery-speed-only, your-turn-only, and
        // stack-must-be-empty. ctx.LandPlayAvailable folds in the per-turn
        // drop cap (computed from the live LandDropTracker by the priority
        // loop), so we no longer over-include PlayLand once the drop is spent.
        // Over-including used to make the auto-pass gate + the fuzz harness
        // propose a land the loop only rejects — flooding logs every round.
        if (sorceryWindow && ctx.LandPlayAvailable && hand.Any(c => c.HasType(CardType.Land)))
        {
            kinds.Add(typeof(PlayLandCommand));
        }

        // CR 302.1 / 307.1 / 117.1a — spells need either sorcery speed
        // (own main + empty stack) for vanilla cards or instant speed
        // (Instant card type or Flash keyword) anytime. Skip the
        // mana-source check entirely: the user might legitimately want
        // to float mana / activate a ritual first.
        var hasCastable = hand.Any(c =>
            !c.HasType(CardType.Land)
            && (sorceryWindow || IsInstantSpeed(c)));
        if (hasCastable)
        {
            kinds.Add(typeof(CastSpellCommand));
        }

        // CR 605.1a / 605.3a — mana abilities are activated whenever the
        // controller has priority. We gate on IManaAbility.CanActivate()
        // (which already incorporates the summoning-sickness + IsTapped
        // checks for {T}-cost mana abilities — CR 302.6) so a Delighted
        // Halfling that just came down doesn't keep prompting a dead
        // window. False NEGATIVES would lock the user out, so we err on
        // the permissive side: if any mana source plausibly CanActivate,
        // surface the kind and let the engine validate on submit.
        var battlefield = ctx.Self.Zones.Battlefield.GetCards();
        var hasManaSource = battlefield.Any(c =>
            c.Abilities.OfType<IManaAbility>().Any(a => a.CanActivate()));
        if (hasManaSource)
        {
            kinds.Add(typeof(ActivateManaAbilityCommand));
        }

        // CR 602.1a — non-mana activated abilities (fetchland sacrifice,
        // equip, planeswalker loyalty, "tap: do X", etc.). IActivatedAbility
        // doesn't expose a CanActivate predicate today, so apply a
        // conservative source-level narrowing: skip a source only when NONE of
        // its non-mana activated abilities is currently usable.
        //
        // A {T} cost (CR 302.6 / 605.3a) can't be paid by a TAPPED permanent or
        // a summoning-sick non-haste CREATURE (CR 302.6). But an ability with
        // NO {T} cost (Yawgmoth's "Pay 1 life, Sacrifice another creature";
        // any "{2}: do X") is usable even on a sick / tapped creature — the
        // earlier blanket skip wrongly hid those, so a sick Yawgmoth's
        // ActivateAbilityCommand was dropped and the activation 400'd. Narrow
        // per-ABILITY: an ability is "currently plausibly usable" unless it
        // taps its own (sick / already-tapped) source.
        var hasActivatedAbility = battlefield.Any(c =>
            c.Abilities.OfType<IActivatedAbility>()
                .Where(a => a is not IManaAbility)
                .Any(a => AbilityPlausiblyUsable(c, a)));
        if (hasActivatedAbility)
        {
            kinds.Add(typeof(ActivateAbilityCommand));
        }

        // CR 606.3 — loyalty abilities are sorcery-speed (own main phase,
        // empty stack), once-per-turn per planeswalker, and (for a minus
        // ability) require enough loyalty to pay the cost without dropping
        // below 0 (CR 606.5). Surface the loyalty-activation kind only when
        // the active player controls a planeswalker (or effective planeswalker)
        // with at least one CURRENTLY-activatable loyalty ability. LoyaltyAbility
        // .CanActivate() already wraps the once-per-turn + payability checks; the
        // sorcery-speed window is the same `sorceryWindow` gate the spell/land
        // kinds use. Excluding it outside that window stops the portal from
        // offering loyalty at instant speed / on a non-empty stack / on the
        // opponent's turn / when the cost can't be paid.
        if (sorceryWindow && battlefield.Any(c =>
                c.Abilities.OfType<LoyaltyAbility>().Any(la => la.CanActivate())))
        {
            kinds.Add(typeof(ActivateLoyaltyAbilityCommand));
        }

        return kinds.ToArray();
    }

    /// <summary>
    /// True iff the only legal priority command at this moment is
    /// <see cref="PassPriorityCommand"/> — the engine has detected a
    /// "dead" priority window (no lands, spells, abilities to do).
    /// Used by <see cref="Majik.Core.Game.PriorityLoop"/>'s auto-pass
    /// gate. Mirrors the portal's <c>isPassOnlyPriorityPrompt</c>.
    /// </summary>
    public static bool IsPassOnly(Type[] kinds)
        => kinds.Length == 1 && kinds[0] == typeof(PassPriorityCommand);

    /// <summary>
    /// Conservative per-ability narrowing for the <see cref="ActivateAbilityCommand"/>
    /// kind: an activated ability on <paramref name="source"/> is plausibly
    /// usable right now UNLESS it carries a {T} cost (an
    /// <see cref="Majik.Core.Costs.AdditionalCost"/> of type
    /// <see cref="Majik.Core.Costs.AdditionalCostType.Tap"/> tapping the source)
    /// that the source can't currently pay because it is already tapped or a
    /// summoning-sick non-haste creature (CR 302.6). Abilities with no {T} cost
    /// (Yawgmoth's "Pay 1 life, Sacrifice another creature"; "{2}: do X") stay
    /// usable on a sick / tapped creature.
    /// </summary>
    private static bool AbilityPlausiblyUsable(ICard source, IActivatedAbility ability)
    {
        var tapsSelf = ability.Costs.OfType<Majik.Core.Costs.AdditionalCost>().Any(c =>
            c.CostType == Majik.Core.Costs.AdditionalCostType.Tap
            && (c.Permanent == null || ReferenceEquals(c.Permanent, source)));
        if (!tapsSelf) return true; // no self-tap requirement — usable.

        if (source is Majik.Core.Cards.Permanent p && p.IsTapped) return false;
        if (source is Majik.Core.Cards.Creature cr
            && cr.HasSummoningSickness
            && !cr.Abilities.OfType<KeywordAbility>().Any(k =>
                string.Equals(k.Keyword, "Haste", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return true;
    }

    /// <summary>Card is castable at instant speed: Instant card type, or
    /// any card with the Flash keyword (CR 702.8).</summary>
    private static bool IsInstantSpeed(ICard card)
    {
        if (card.HasType(CardType.Instant)) return true;
        return card.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
    }
}
