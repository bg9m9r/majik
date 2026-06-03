using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Semester's End (Strixhaven, {3}{W}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Exile any number of target creatures and/or planeswalkers you control.
///    At the beginning of the next end step, return each of them to the
///    battlefield under its owner's control. Each of them enters with an
///    additional +1/+1 counter on it if it's a creature and an additional
///    loyalty counter on it if it's a planeswalker."
///
/// CR 701.21 (exile) + CR 603.7 (delayed triggered ability) + CR 614 ("under
/// its owner's control") + CR 122.1b/c (loyalty / +1/+1 counters). This is the
/// "for many" exile-with-delayed-return-plus-counter spell: a single
/// multi-target slot ("any number of") over BOTH creatures and planeswalkers,
/// every member exiled together and returned as one batch by one delayed end-
/// step trigger — each re-entering with a type-appropriate counter. The signature
/// use is dodging a board wipe / Damnation while keeping a permanent pump (and
/// resetting / bumping your planeswalkers' loyalty).
///
/// ## Declarative spell schema (the <c>exile_with_return</c> verb + counter rider)
/// <see cref="BuildDefinition"/> declares a single
/// <see cref="ExileWithReturnEffectDef"/> verb (filter
/// <c>"creature_or_planeswalker_you_control"</c>, <c>MinTargets: 0</c> /
/// <c>MaxTargets</c> large = "any number of", <c>ReturnAt: "next_end_step"</c>,
/// <c>CounterOnReturn: "plus_one_plus_one_or_loyalty"</c>) and routes it through
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the same #2128
/// spell adapter the rest of the declarative spell family uses, built on the
/// #2470 <c>exile_with_return</c> primitive (Eerie Interlude). The type-aware
/// counter rider (#2470 follow-up) mints a +1/+1 counter on each returned
/// creature and a loyalty counter on each returned planeswalker as the linked
/// CR 603.7 delayed trigger returns the batch.
///
/// ## Notes
/// - "any number of" — declining (zero targets) is legal (CR 115.1b) and
///   resolves to a clean no-op (nothing exiled, no return scheduled).
/// - "you control" is enforced at gather time by the shared
///   <see cref="TargetFilters"/> control rider; the CR 608.2b resolution
///   re-check fizzles any pick that has left the battlefield.
/// - The return is "under its owner's control" (CR 614).
/// - The loyalty counter is ADDITIVE on top of the planeswalker's fresh
///   starting loyalty (it re-enters as a new object at its starting loyalty,
///   CR 613 / 306.5b, then the rider adds one — net +1 over printed).
/// - Counter placement routes through the <see cref="ReplacementBus"/> when
///   present (Doubling Season / Hardened Scales / Vorinclex).
/// - Shape-only fallback: with no registered
///   <see cref="Majik.Core.Abilities.TriggerManager"/> the exile still fires but
///   the delayed return (and its counters) is skipped — the same two-mode
///   posture as Eerie Interlude / Otherworldly Journey.
/// </summary>
[CardName("Semester's End")]
public static class SemestersEndFactory
{
    public const string CardName = "Semester's End";
    public const string PrintedManaCost = "{3}{W}";

    /// <summary>Construct Semester's End as an Instant owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve body is produced
    /// by <see cref="BuildDefinition"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "exile any number of target creatures and/or planeswalkers you
    /// control; return them at the next end step, each with a +1/+1 (creature)
    /// or loyalty (planeswalker) counter" <see cref="SpellDefinition"/>
    /// declaratively (the <c>exile_with_return</c> verb + counter rider). The
    /// single multi-target slot is <c>0..many</c> "creatures and/or
    /// planeswalkers you control".
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new ExileWithReturnEffectDef
                {
                    TargetFilter = "creature_or_planeswalker_you_control",
                    MinTargets = 0,
                    // "any number of" — practical upper bound large enough for
                    // any real board; the gatherer offers only legal picks.
                    MaxTargets = 99,
                    ReturnAt = "next_end_step",
                    CounterOnReturn = "plus_one_plus_one_or_loyalty",
                },
            });
}
