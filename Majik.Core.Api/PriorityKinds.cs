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
        // stack-must-be-empty. The per-turn cap isn't checked here —
        // overinclude when the window is right and there's a land in
        // hand; the engine's LandDropTracker rejects an over-cap
        // submission cleanly.
        if (sorceryWindow && hand.Any(c => c.HasType(CardType.Land)))
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
        // conservative source-level narrowing: a tapped permanent or a
        // summoning-sick non-haste creature can't pay a {T} cost (the
        // overwhelmingly common shape) — skip those sources. Permanents
        // with non-tap activated abilities (e.g. {2}: do X) that happen
        // to be sick are a rare false-negative; the trade-off heavily
        // favours killing the per-step prompt spam on a board of just
        // summoning-sick creatures.
        var hasActivatedAbility = battlefield.Any(c =>
        {
            if (!c.Abilities.OfType<IActivatedAbility>().Any(a => a is not IManaAbility))
            {
                return false;
            }
            if (c is Majik.Core.Cards.Permanent p && p.IsTapped) return false;
            if (c is Majik.Core.Cards.Creature cr
                && cr.HasSummoningSickness
                && !cr.Abilities.OfType<KeywordAbility>().Any(k =>
                    string.Equals(k.Keyword, "Haste", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            return true;
        });
        if (hasActivatedAbility)
        {
            kinds.Add(typeof(ActivateAbilityCommand));
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

    /// <summary>Card is castable at instant speed: Instant card type, or
    /// any card with the Flash keyword (CR 702.8).</summary>
    private static bool IsInstantSpeed(ICard card)
    {
        if (card.HasType(CardType.Instant)) return true;
        return card.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
    }
}
