using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eerie Interlude (Shadows over Innistrad, {2}{W}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Exile any number of target creatures you control. Return those cards
///    to the battlefield under their owner's control at the beginning of the
///    next end step."
///
/// CR 701.21 (exile) + CR 603.7 (delayed triggered ability) + CR 614
/// ("under its owner's control") — the canonical "for many" flicker-with-
/// delayed-return spell. Cast in response to a board wipe (Wrath of God,
/// Damnation), your creatures dodge it in exile and all return together at
/// the next end step. Distinct from <see cref="OtherworldlyJourneyFactory"/>
/// (single target, +1/+1 counter on return) and Cloudshift / Ephemerate
/// (single target, immediate return). The defining feature is the SINGLE
/// multi-target slot: one spell, "any number of" creatures, all exiled and
/// returned as one batch by one delayed trigger.
///
/// ## Declarative spell schema (the <c>exile_with_return</c> verb)
/// <see cref="BuildDefinition"/> declares a single
/// <see cref="ExileWithReturnEffectDef"/> verb (filter
/// <c>"creature_you_control"</c>, <c>MinTargets: 0</c> / <c>MaxTargets</c>
/// large = "any number of", <c>ReturnAt: "next_end_step"</c>) and routes it
/// through <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the
/// same #2128 spell adapter the rest of the declarative spell family uses. The
/// verb exiles the whole chosen batch on resolution (CR 601.2c reads every
/// pick in the slot), records each card + owner, and registers ONE delayed
/// end-step return (CR 603.7) via the live game's
/// <see cref="Majik.Core.Abilities.TriggerManager"/> (resolved from the
/// per-game <see cref="Majik.Core.Abilities.TriggerManagerRegistry"/>).
///
/// ## Notes
/// - "any number of" — declining (zero targets) is legal (CR 115.1b) and
///   resolves to a clean no-op (nothing exiled, nothing scheduled).
/// - "you control" is enforced at gather time by the shared
///   <see cref="TargetFilters"/> control rider, and the CR 608.2b
///   resolution re-check fizzles any pick that has left the battlefield.
/// - The return is "under its owner's control" (CR 614) — an Act-of-Treason'd
///   creature you'd exiled would come back to its true owner, not to you.
/// - Shape-only fallback: with no registered <see cref="Majik.Core.Abilities.TriggerManager"/>
///   the exile still fires but the delayed return is skipped (same two-mode
///   posture as <see cref="OtherworldlyJourneyFactory"/>).
/// </summary>
[CardName("Eerie Interlude")]
public static class EerieInterludeFactory
{
    public const string CardName = "Eerie Interlude";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>Construct Eerie Interlude as an Instant owned and controlled
    /// by <paramref name="owner"/>. Card shape only — the resolve body is
    /// produced by <see cref="BuildDefinition"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "exile any number of target creatures you control; return
    /// them at the next end step" <see cref="SpellDefinition"/> declaratively
    /// (the <c>exile_with_return</c> verb). The single multi-target slot is
    /// <c>0..many</c> "creatures you control".
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new ExileWithReturnEffectDef
                {
                    TargetFilter = "creature_you_control",
                    MinTargets = 0,
                    // "any number of" — practical upper bound large enough for
                    // any real board; the gatherer offers only legal picks.
                    MaxTargets = 99,
                    ReturnAt = "next_end_step",
                },
            });
}
