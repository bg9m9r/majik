using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card <c>Emeria's Call // Emeria, Shattered Skyclave</c>
/// ({4}{W}{W}{W}).
///
/// Oracle text (verified against Scryfall):
///   Front — Emeria's Call, Sorcery {4}{W}{W}{W}:
///     "Create two 4/4 white Angel Warrior creature tokens with flying.
///      Non-Angel creatures you control gain indestructible until your next
///      turn."
///   Back — Emeria, Shattered Skyclave, Land:
///     "As this land enters, you may pay 3 life. If you don't, it enters
///      tapped." / "{T}: Add {W}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Emeria's Call // Emeria, Shattered Skyclave"). The two individual faces
/// are already implemented by <see cref="EmeriasCallFactory"/> (front) and
/// <see cref="EmeriaShatteredSkyclaveFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/> and
/// <see cref="AgadeemsAwakeningAgadeemTheUndercryptFactory"/>.
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand is the castable spell half, so the
/// combined name resolves to the FRONT face (Emeria's Call, a Sorcery). The
/// front-face builder already attaches an <see cref="MdfcState"/> carrying a
/// castable back-face <see cref="MdfcFace.Land"/> descriptor (so
/// <c>MdfcCastFlow</c> can offer "play as the land Emeria, Shattered Skyclave"
/// with its "pay 3 life or enters tapped" ETB — CR 614.1c). This arm simply
/// delegates to <see cref="EmeriasCallFactory.Create"/> so the
/// cast-either-face wiring is defined in exactly one place.
/// </summary>
[CardName("Emeria's Call // Emeria, Shattered Skyclave")]
public static class EmeriasCallEmeriaShatteredSkyclaveFactory
{
    public const string CardName = "Emeria's Call // Emeria, Shattered Skyclave";
    public const string Slug = "emerias-call-emeria-shattered-skyclave";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Emeria's Call,
    /// Sorcery {4}{W}{W}{W}) with the <see cref="MdfcState"/> face tracker +
    /// castable back-face Land descriptor attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + resolve-time
    /// behaviour come from <see cref="EmeriasCallFactory"/> so the
    /// cast-either-face logic lives in one place.
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
        return EmeriasCallFactory.Create(owner);
    }
}
