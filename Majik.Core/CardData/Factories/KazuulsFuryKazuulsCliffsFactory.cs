using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card <c>Kazuul's Fury // Kazuul's Cliffs</c> ({2}{R}).
///
/// Oracle text (verified against Scryfall):
///   Front — Kazuul's Fury, Instant {2}{R}:
///     "As an additional cost to cast this spell, sacrifice a creature.
///      Kazuul's Fury deals damage equal to the sacrificed creature's power
///      to any target."
///   Back — Kazuul's Cliffs, Land:
///     "This land enters tapped." / "{T}: Add {R}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Kazuul's Fury // Kazuul's Cliffs"). The two individual faces are already
/// implemented by <see cref="KazuulsFuryFactory"/> (front) and
/// <see cref="KazuulsCliffsFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand is the castable spell half, so the
/// combined name resolves to the FRONT face (Kazuul's Fury, an Instant). The
/// front-face builder already attaches an <see cref="MDFCs.MdfcState"/> carrying
/// a castable back-face <see cref="MDFCs.MdfcFace.Land"/> descriptor (so the
/// MDFC cast flow can offer "play as the land Kazuul's Cliffs" with its
/// unconditional "this land enters tapped" ETB — CR 614.1c). This arm simply
/// delegates to <see cref="KazuulsFuryFactory.Create"/> so the
/// cast-either-face wiring is defined in exactly one place.
/// </summary>
[CardName("Kazuul's Fury // Kazuul's Cliffs")]
public static class KazuulsFuryKazuulsCliffsFactory
{
    public const string CardName = "Kazuul's Fury // Kazuul's Cliffs";
    public const string Slug = "kazuuls-fury-kazuuls-cliffs";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Kazuul's Fury,
    /// Instant {2}{R}) with the <see cref="MDFCs.MdfcState"/> face tracker +
    /// castable back-face Land descriptor attached.
    ///
    /// The Load call validates the combined-slug JSON resource exists; the
    /// returned card is then discarded in favour of the fully-wired front-face
    /// instance built by <see cref="KazuulsFuryFactory.Create"/> so the
    /// cast-either-face logic lives in exactly one place.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity from the combined-name JSON (front-face characteristics).
        _ = CardDefinitionLoader.FromEmbeddedResource(Slug);

        // Delegate to the front-face factory — it builds the Instant and
        // attaches the MdfcState with the castable back-face Land descriptor
        // (CR 712.3 — cast-either-face).
        return KazuulsFuryFactory.Create(owner);
    }
}
