using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scion of Oona (Lorwyn, {1}{U}).
///
/// Creature — Faerie Soldier 1/1. Oracle text:
///   "Flash
///    Flying
///    Other Faerie creatures you control get +1/+1 and have shroud."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Faerie Soldier at {1}{U} with Flash (CR 702.8) + Flying
///   (CR 702.9) keyword markers.
/// - <b>Static "Other Faerie creatures you control get +1/+1 and have
///   shroud"</b> wired via <see cref="LordStaticEffect"/>:
///   <c>matchingSubtype: Faerie</c>, <c>power: 1, toughness: 1</c>,
///   <c>grantedKeywords: ["Shroud"]</c>, <c>includeSelf: false</c>,
///   controller-scoped (default — not <c>opponentsOnly</c>, not
///   <c>allPlayers</c>). Layer 7c for P/T (CR 613.7c) + Layer 6 for the
///   granted keyword (CR 613.1f). The LordStaticEffect MVP places both at
///   <see cref="Layer.PT_Modify"/> — same posture as Goblin Chieftain /
///   Lord of Atlantis.
///
/// ## Shroud grant (CR 702.18)
/// The "Shroud" string is added to each matching Faerie's effective keyword
/// set via <see cref="CreatureCharacteristics.Keywords"/>. The
/// <see cref="Majik.Core.Targeting.TargetLegality"/> path reads Shroud from
/// the creature's effective keywords — same shape as Creeping Tar Pit and
/// Sterling Grove. Note: the Scion itself does NOT gain Shroud from its
/// own static ("Other" — <c>includeSelf: false</c>), so it remains a legal
/// removal target. This is the canonical Scion-of-Oona play pattern.
///
/// ## Multiple Scions stack
/// Two Scions on the battlefield give Other Faeries +2/+2 (and each grants
/// Shroud — idempotent via <see cref="HashSet{T}"/> semantics in
/// <see cref="CreatureCharacteristics.Keywords"/>). Each Scion's static
/// still applies to the OTHER Scion ("Other" excludes only self vs self).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Scion isn't on the battlefield so the bonus lifts correctly. Same
///   caveat as the other LordStaticEffect-based factories.
/// </summary>
[CardName("Scion of Oona")]
public static class ScionOfOonaFactory
{
    public const string CardName = "Scion of Oona";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Scion of Oona with no live continuous-effects service.
    /// Flash + Flying are wired; the lord static effect is not registered.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Scion of Oona. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and Shroud to other
    /// Faerie creatures the Scion's controller controls is registered
    /// against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 + Shroud static effect against. May be null — no live
    /// bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 702.9 — Flying. Combat blocking restriction.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted keyword) — "Other Faerie
            // creatures you control get +1/+1 and have shroud." includeSelf
            // is false so the Scion itself doesn't gain Shroud + doesn't
            // double-stack the +1/+1 — it stays a legal removal target,
            // which is the canonical UB Faeries combat-trick play. Scoped
            // to the controller's battlefield (default filter).
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Faerie,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Shroud" },
                includeSelf: false,
                opponentsOnly: false));
        }

        return card;
    }
}
