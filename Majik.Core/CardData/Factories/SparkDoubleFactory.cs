using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spark Double (War of the Spark, {3}{U}).
///
/// ## Card text (verified against Scryfall 2026-06-02)
/// Creature — Illusion, 0/0.
///   "You may have this creature enter as a copy of a creature or planeswalker
///    you control, except it enters with an additional +1/+1 counter on it if
///    it's a creature, it enters with an additional loyalty counter on it if
///    it's a planeswalker, and it isn't legendary."
///
/// ## Implemented (v1)
/// - 0/0 Creature — Illusion, mana cost {3}{U}. Printed 0/0 per CR 706.9 — the
///   copy overwrites P/T; if it doesn't copy, the 0/0 dies to SBA (CR 704.5f).
/// - <b>Enters-as-copy of a creature OR planeswalker you control (CR 706.9)</b>
///   via the shared generalized <see cref="EntersAsCopyReplacement"/> with
///   <see cref="EntersAsCopyReplacement.SourceFilter.CreatureOrPlaneswalker"/>
///   and pool <see cref="EntersAsCopyReplacement.CopyPool.BattlefieldYouControl"/>.
/// - <b>"+1/+1 counter if it's a creature" (CR 706.9b)</b> via
///   <see cref="EntersAsCopyReplacement.Options.PlusOneCounterIfCopiedCreature"/>
///   — rides through <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> so the
///   counter lands after the copy is placed (base 2/2 copy + 1/1 counter → 3/3).
/// - <b>"isn't legendary" (CR 706.2)</b> via
///   <see cref="EntersAsCopyReplacement.Options.StripLegendary"/> (Layer-4
///   <see cref="RemoveSupertypeEffect"/>) so copying a Legendary creature does
///   not trip the legend rule.
///
/// ## Deferred (v1 gaps)
/// - <b>Planeswalker copy source — loyalty counter rider</b>: the engine tracks
///   loyalty on <see cref="Planeswalker.Loyalty"/> rather than the generic
///   counters collection, and a Spark Double built as a <see cref="Creature"/>
///   C# instance cannot itself become a Planeswalker through the
///   characteristics-row copy (the manland-P/T-style instance gap documented on
///   <see cref="CopyCharacteristicsEffect"/>). So Spark Double copying a
///   planeswalker you control is NOT fully surfaced at v1; the
///   <see cref="EntersAsCopyReplacement.Options.LoyaltyCounterIfCopiedPlaneswalker"/>
///   rider is wired and applies when the entering instance is itself a
///   Planeswalker, but the common-case creature copy is the supported path.
///   (Recorded in v1-deferrals.)
/// - "You may" choice — auto-yes when any candidate exists (shared
///   <see cref="EntersAsCopyReplacement"/> posture).
/// </summary>
[CardName("Spark Double")]
public static class SparkDoubleFactory
{
    public const string CardName = "Spark Double";

    /// <summary>Shape-only overload dispatched by <see cref="NamedCardFactory"/>.</summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, effects: null);

    /// <summary>
    /// Construct Spark Double with optional replacement-bus + continuous-effects
    /// wiring. When both are supplied the generalized enters-as-copy replacement
    /// (CR 706.9) + the conditional +1/+1 counter (CR 706.9b) + the
    /// not-legendary strip (CR 706.2) are registered.
    /// </summary>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{3}{U}",
            power: 0,
            toughness: 0,
            subtypes: new[] { CardSubtype.Illusion });

        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null && effects != null)
        {
            replacements.Register(new EntersAsCopyReplacement(
                card,
                EntersAsCopyReplacement.CopyPool.BattlefieldYouControl,
                effects,
                new EntersAsCopyReplacement.Options(
                    Filter: EntersAsCopyReplacement.SourceFilter.CreatureOrPlaneswalker,
                    StripLegendary: true,
                    PlusOneCounterIfCopiedCreature: true,
                    LoyaltyCounterIfCopiedPlaneswalker: true)));

            card.ActiveEffects = effects;
        }

        return card;
    }
}
