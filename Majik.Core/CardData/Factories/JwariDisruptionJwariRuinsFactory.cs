using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card <c>Jwari Disruption // Jwari Ruins</c> ({1}{U}).
///
/// Oracle text (verified against Scryfall):
///   Front — Jwari Disruption, Instant {1}{U}:
///     "Counter target spell unless its controller pays {1}."
///   Back — Jwari Ruins, Land:
///     "This land enters tapped." / "{T}: Add {U}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Jwari Disruption // Jwari Ruins"). The two individual faces are already
/// implemented by <see cref="JwariDisruptionFactory"/> (front) and
/// <see cref="JwariRuinsFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand is the castable spell half, so the
/// combined name resolves to the FRONT face (Jwari Disruption, an Instant). The
/// front-face builder already attaches an <see cref="MdfcState"/> carrying a
/// castable back-face <see cref="MdfcFace.Land"/> descriptor (so
/// <c>MdfcCastFlow</c> can offer "play as the land Jwari Ruins" with its
/// unconditional "this land enters tapped" ETB — CR 614.1c). This arm simply
/// delegates to <see cref="JwariDisruptionFactory.Create"/> so the
/// cast-either-face wiring is defined in exactly one place.
/// </summary>
[CardName("Jwari Disruption // Jwari Ruins")]
public static class JwariDisruptionJwariRuinsFactory
{
    public const string CardName = "Jwari Disruption // Jwari Ruins";
    public const string Slug = "jwari-disruption-jwari-ruins";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Jwari
    /// Disruption, Instant {1}{U}) with the <see cref="MdfcState"/> face tracker
    /// + castable back-face Land descriptor attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + resolve-time
    /// counter behaviour come from <see cref="JwariDisruptionFactory"/> so the
    /// cast-either-face logic lives in one place.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity from the combined-name JSON (front-face characteristics).
        // The Load call validates the slug resource exists; the returned card
        // is then discarded in favour of the fully-wired front-face instance
        // below so the MDFC cast-either-face descriptor is attached in exactly
        // one place.
        _ = CardDefinitionLoader.FromEmbeddedResource(Slug);

        // Delegate to the front-face factory — it builds the Instant and
        // attaches the MdfcState with the castable back-face Land descriptor
        // (CR 712.3 — cast-either-face).
        return JwariDisruptionFactory.Create(owner);
    }
}
