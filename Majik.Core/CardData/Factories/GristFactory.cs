using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grist, the Hunger Tide (Streets of New Capenna, {1}{B}{G}).
///
/// Legendary Planeswalker — Grist, loyalty 3.
/// Oracle text:
///   "As long as Grist, the Hunger Tide isn't on the battlefield, it's a
///    1/1 Insect creature in addition to its other types."
///   [+1], [-2], [-5] loyalty abilities wired by OracleLoyaltyAbilityBinder
///   during the deck-load pipeline — this factory sets up card structure only.
///
/// ## V1 simplification
/// The "not on the battlefield" conditional is deferred. Grist is constructed
/// as a Planeswalker with <see cref="CardType.Creature"/> unconditionally added,
/// plus the Insect subtype. This makes Grist tutorable by Green Sun's Zenith
/// and similar creature-search effects (HasType(Creature) == true) without
/// wiring the full conditional layer-4 infrastructure.
///
/// Combat math against Grist (P/T) will not behave correctly — Planeswalker
/// does not expose a settable P/T — but planeswalker combat damage uses the
/// loyalty track, not P/T, so this is acceptable for v1.
///
/// The conditional layer-4 effect ("only when not on battlefield") is
/// documented but deferred to a future slice.
/// </summary>
public static class GristFactory
{
    /// <summary>
    /// Construct Grist, the Hunger Tide for the given owner.
    /// Returns a <see cref="Planeswalker"/> with Creature type added
    /// (see class xmldoc for the v1 deviation from oracle text).
    /// </summary>
    public static Planeswalker Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var grist = new Planeswalker(
            name: "Grist, the Hunger Tide",
            manaCost: "{1}{B}{G}",
            startingLoyalty: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Grist, CardSubtype.Insect });

        // V1: add Creature type unconditionally so tutors like Green Sun's
        // Zenith can target Grist in all zones (rule 115.4 / 106.5a).
        // The oracle-text restriction ("as long as … isn't on the battlefield")
        // is a conditional layer-4 effect — deferred.
        grist.AddCardType(CardType.Creature);

        grist.SetOwner(owner);
        grist.SetController(owner);

        return grist;
    }
}
