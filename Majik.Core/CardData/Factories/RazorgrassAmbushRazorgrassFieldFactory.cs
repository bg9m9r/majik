using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Modern Horizons 3
/// modal double-faced card <c>Razorgrass Ambush // Razorgrass Field</c>
/// ({1}{W}).
///
/// Oracle text (verified against Scryfall):
///   Front — Razorgrass Ambush, Instant {1}{W}:
///     "Razorgrass Ambush deals 3 damage to target attacking or blocking
///      creature."
///   Back — Razorgrass Field, Land:
///     "As this land enters, you may pay 3 life. If you don't, it enters
///      tapped." / "{T}: Add {W}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Razorgrass Ambush // Razorgrass Field"). The two individual faces are
/// already implemented by <see cref="RazorgrassAmbushFactory"/> (front) and
/// <see cref="RazorgrassFieldFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand is the castable spell half, so the
/// combined name resolves to the FRONT face (Razorgrass Ambush, an Instant).
/// The front-face builder already attaches an <see cref="MDFCs.MdfcState"/>
/// carrying a castable back-face <see cref="MDFCs.MdfcFace.Land"/> descriptor
/// (so <c>MdfcCastFlow</c> can offer "play as the land Razorgrass Field" with
/// its "you may pay 3 life; if you don't, it enters tapped" ETB —
/// CR 614.1c). This arm simply delegates to
/// <see cref="RazorgrassAmbushFactory.Create"/> so the cast-either-face wiring
/// is defined in exactly one place.
///
/// Unlike <see cref="SilundiVisionSilundiIsleFactory"/>, the front face
/// builds its identity in code (no front-face JSON exists), so this arm has no
/// JSON resource to load — it delegates directly.
/// </summary>
[CardName("Razorgrass Ambush // Razorgrass Field")]
public static class RazorgrassAmbushRazorgrassFieldFactory
{
    public const string CardName = "Razorgrass Ambush // Razorgrass Field";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Razorgrass
    /// Ambush, Instant {1}{W}) with the <see cref="MDFCs.MdfcState"/> face
    /// tracker + castable back-face Land descriptor attached.
    ///
    /// Delegates to <see cref="RazorgrassAmbushFactory.Create"/> so the
    /// cast-either-face logic lives in exactly one place (CR 712.3).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return RazorgrassAmbushFactory.Create(owner);
    }
}
