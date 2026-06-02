using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Metamorph (New Phyrexia / Double Masters,
/// {3}{U/P}).
///
/// ## Card text (verified against Scryfall 2026-06-02)
/// Artifact Creature — Phyrexian Shapeshifter, 0/0.
///   "({U/P} can be paid with either {U} or 2 life.)
///    You may have this creature enter as a copy of any artifact or creature on
///    the battlefield, except it's an artifact in addition to its other types."
///
/// ## Implemented (v1)
/// - 0/0 Artifact Creature — Phyrexian Shapeshifter, mana cost {3}{U/P}.
///   Printed 0/0 per CR 706.9 — Metamorph's printed P/T is overwritten by the
///   copy when it enters; if it doesn't copy, the 0/0 dies to SBA (CR 704.5f).
/// - <b>Enters-as-copy of any artifact OR creature (CR 706.9)</b> via the
///   shared generalized <see cref="EntersAsCopyReplacement"/> with
///   <see cref="EntersAsCopyReplacement.SourceFilter.ArtifactOrCreature"/> and
///   pool <see cref="EntersAsCopyReplacement.CopyPool.AnyBattlefield"/>. The
///   generalized path registers a <see cref="CopyCharacteristicsEffect"/>
///   (CR 707.2) so a NONCREATURE artifact is a legal copy source — closing the
///   gap that the legacy creature-only <see cref="CopyEffect"/> path left open.
/// - <b>"it's an artifact in addition to its other types" (CR 706.9c /
///   613.1d)</b> via <see cref="EntersAsCopyReplacement.Options.AddTypeOnCopy"/>
///   = <see cref="CardType.Artifact"/> — a Layer-4 <see cref="AddCardTypeEffect"/>
///   re-adds Artifact on top of whatever was copied (so copying a non-artifact
///   creature still leaves Metamorph an Artifact).
///
/// ## Deferred (v1 gaps)
/// - "You may" choice — auto-yes when any candidate exists (shared
///   <see cref="EntersAsCopyReplacement"/> posture; tests model decline via an
///   empty battlefield).
/// - The {U/P} Phyrexian mana cost is carried as the printed string; the
///   "pay 2 life" alternative payment is the engine-wide Phyrexian-mana posture
///   (out of scope here).
/// </summary>
[CardName("Phyrexian Metamorph")]
public static class PhyrexianMetamorphFactory
{
    public const string CardName = "Phyrexian Metamorph";

    /// <summary>Shape-only overload dispatched by <see cref="NamedCardFactory"/>.</summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, effects: null);

    /// <summary>
    /// Construct Phyrexian Metamorph with optional replacement-bus +
    /// continuous-effects wiring. When both are supplied the generalized
    /// enters-as-copy replacement (CR 706.9) + the "Artifact in addition"
    /// rider (CR 706.9c) are registered.
    /// </summary>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed: Artifact Creature — Phyrexian Shapeshifter {3}{U/P}, 0/0.
        var card = new Creature(
            name: CardName,
            manaCost: "{3}{U/P}",
            power: 0,
            toughness: 0,
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Shapeshifter });
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null && effects != null)
        {
            // CR 706.9 — enter as a copy of any artifact OR creature on the
            // battlefield, "except it's an artifact in addition to its other
            // types" (CR 706.9c). The generalized path copies full
            // characteristics (CR 707.2) so a noncreature artifact source works.
            replacements.Register(new EntersAsCopyReplacement(
                card,
                EntersAsCopyReplacement.CopyPool.AnyBattlefield,
                effects,
                new EntersAsCopyReplacement.Options(
                    Filter: EntersAsCopyReplacement.SourceFilter.ArtifactOrCreature,
                    AddTypeOnCopy: CardType.Artifact)));

            card.ActiveEffects = effects;
        }

        return card;
    }
}
