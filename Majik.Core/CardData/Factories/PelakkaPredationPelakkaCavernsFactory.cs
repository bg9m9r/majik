using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card <c>Pelakka Predation // Pelakka Caverns</c>
/// ({2}{B}).
///
/// Oracle text (verified against Scryfall):
///   Front — Pelakka Predation, Sorcery {2}{B}:
///     "Target opponent reveals their hand. You choose a card from it with
///      mana value 3 or greater. That player discards that card."
///   Back — Pelakka Caverns, Land:
///     "This land enters tapped." / "{T}: Add {B}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Pelakka Predation // Pelakka Caverns"). The two individual faces are
/// already implemented by <see cref="PelakkaPredationFactory"/> (front) and
/// <see cref="PelakkaCavernsFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand is the castable spell half, so the
/// combined name resolves to the FRONT face (Pelakka Predation, a Sorcery).
/// The front-face builder already attaches an <see cref="MDFCs.MdfcState"/>
/// carrying a castable back-face <see cref="MDFCs.MdfcFace.Land"/> descriptor
/// (so <c>MdfcCastFlow</c> can offer "play as the land Pelakka Caverns" with
/// its unconditional "this land enters tapped" ETB — CR 614.1c). This arm
/// simply delegates to <see cref="PelakkaPredationFactory.Create"/> so the
/// cast-either-face wiring is defined in exactly one place.
/// </summary>
[CardName("Pelakka Predation // Pelakka Caverns")]
public static class PelakkaPredationPelakkaCavernsFactory
{
    public const string CardName = "Pelakka Predation // Pelakka Caverns";
    public const string Slug = "pelakka-predation-pelakka-caverns";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Pelakka
    /// Predation, Sorcery {2}{B}) with the <see cref="MDFCs.MdfcState"/> face
    /// tracker + castable back-face Land descriptor attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + resolve-time
    /// reveal/choose/discard behaviour come from
    /// <see cref="PelakkaPredationFactory"/> so the cast-either-face logic
    /// lives in one place.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity from the combined-name JSON (front-face characteristics).
        // The Load call validates the slug resource exists; the returned card
        // is then discarded in favour of the fully-wired front-face instance
        // below so the MDFC cast-either-face descriptor is attached in exactly
        // one place.
        _ = CardDefinitionLoader.FromEmbeddedResource(Slug);

        // Delegate to the front-face factory — it builds the Sorcery and
        // attaches the MdfcState with the castable back-face Land descriptor
        // (CR 712.3 — cast-either-face).
        return PelakkaPredationFactory.Create(owner);
    }
}
