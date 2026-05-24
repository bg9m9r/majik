using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Master of the Pearl Trident (Magic 2013 / reprints,
/// Creature — Merfolk {U}{U} 2/2).
///
/// Oracle text:
///   "Other Merfolk you control get +1/+1 and have Islandwalk."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Merfolk, mana cost {U}{U}, owner/controller wired.
/// - <b>Static "Other Merfolk you control get +1/+1 and have Islandwalk"</b>
///   wired via <see cref="LordStaticEffect"/>:
///   <c>matchingSubtype: Merfolk</c>, <c>power: 1, toughness: 1</c>,
///   <c>grantedKeywords: ["Islandwalk"]</c>, <c>includeSelf: false</c>,
///   <c>allPlayers: false</c> (controller-scoped).
///   Unlike <see cref="LordOfAtlantisFactory"/>, the "you control" qualifier
///   means only the controller's own Merfolk are buffed — opponents' Merfolk
///   are unaffected. Layer 7c for P/T (CR 613.7c), Layer 6 for the granted
///   keyword (CR 613.1f). This MVP places both at
///   <see cref="Layer.PT_Modify"/>. Layer 7c.
///
/// ## Islandwalk (CR 702.14)
/// The "Islandwalk" string is added to each matching creature's keyword set
/// via <see cref="CreatureCharacteristics.Keywords"/>. The combat-validator
/// enforcement of Islandwalk ("creature can't be blocked as long as the
/// defending player controls an Island") is deferred — same posture as
/// Intimidate / Menace enforcement. The keyword marker is sufficient for
/// the factory to ship.
///
/// ## Deferred (v1 gaps)
/// - Islandwalk combat-enforcement (CR 702.14b) — blocking restriction
///   gate is not yet wired in the combat validator.
/// - LTB unregister — the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Master of the Pearl Trident isn't on the battlefield so the bonus
///   lifts correctly.
/// </summary>
[CardName("Master of the Pearl Trident")]
public static class MasterOfThePearlTridentFactory
{
    public const string CardName = "Master of the Pearl Trident";
    public const string PrintedManaCost = "{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Master of the Pearl Trident without a live continuous-effects
    /// service. Suitable for shape / dispatcher tests — the lord static effect
    /// is not registered. Other Merfolk you control don't yet receive +1/+1 +
    /// Islandwalk because there's no layers service to register the effect
    /// against.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Master of the Pearl Trident. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and Islandwalk to
    /// other Merfolk the controller controls is registered against the
    /// layers service. Opponent's Merfolk are NOT affected (no allPlayers).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 + Islandwalk static effect against. May be null — no live
    /// bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Merfolk });

        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted keywords).
            // "Other Merfolk you control get +1/+1 and have Islandwalk."
            // allPlayers: false → controller-scoped (only the controller's
            // own Merfolk benefit). includeSelf: false honours "Other".
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Merfolk,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Islandwalk" },
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: false));
        }

        return card;
    }
}
