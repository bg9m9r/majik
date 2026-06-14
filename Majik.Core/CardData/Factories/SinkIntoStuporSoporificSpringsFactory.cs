using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Bloomburrow modal
/// double-faced card <c>Sink into Stupor // Soporific Springs</c> ({1}{U}{U}).
///
/// Oracle text (verified against Scryfall):
///   Front — Sink into Stupor, Instant {1}{U}{U}:
///     "Return target spell or nonland permanent an opponent controls to its
///      owner's hand."
///   Back — Soporific Springs, Land:
///     "As this land enters, you may pay 3 life. If you don't, it enters
///      tapped." / "{T}: Add {U}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Sink into Stupor // Soporific Springs"). The two individual faces are
/// already implemented by <see cref="SinkIntoStuporFactory"/> (front) and
/// <see cref="SoporificSpringsFactory"/> (back), but the engine's
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
/// combined name resolves to the FRONT face (Sink into Stupor, an Instant). The
/// front-face builder already attaches an <see cref="MDFCs.MdfcState"/> carrying
/// a castable back-face <see cref="MDFCs.MdfcFace.Land"/> descriptor (so
/// <c>MdfcCastFlow</c> can offer "play as the land Soporific Springs" with its
/// "pay 3 life or enter tapped" ETB — CR 614.1c). This arm simply delegates to
/// <see cref="SinkIntoStuporFactory.Create"/> so the cast-either-face wiring is
/// defined in exactly one place.
/// </summary>
[CardName("Sink into Stupor // Soporific Springs")]
public static class SinkIntoStuporSoporificSpringsFactory
{
    public const string CardName = "Sink into Stupor // Soporific Springs";
    public const string Slug = "sink-into-stupor-soporific-springs";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Sink into
    /// Stupor, Instant {1}{U}{U}) with the <see cref="MDFCs.MdfcState"/> face
    /// tracker + castable back-face Land descriptor attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + resolve-time
    /// bounce behaviour come from <see cref="SinkIntoStuporFactory"/> so the
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
        return SinkIntoStuporFactory.Create(owner);
    }
}
