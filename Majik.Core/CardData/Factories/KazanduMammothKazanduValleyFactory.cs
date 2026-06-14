using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card <c>Kazandu Mammoth // Kazandu Valley</c> ({1}{G}{G}).
///
/// Oracle text (verified against Scryfall):
///   Front — Kazandu Mammoth, Creature — Elephant 3/3 {1}{G}{G}:
///     "Landfall — Whenever a land you control enters, this creature gets
///      +2/+2 until end of turn."
///   Back — Kazandu Valley, Land:
///     "This land enters tapped." / "{T}: Add {G}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Kazandu Mammoth // Kazandu Valley"). The two individual faces are already
/// implemented by <see cref="KazanduMammothFactory"/> (front) and
/// <see cref="KazanduValleyFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/>.
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// cast from hand is the castable spell half, so the combined name resolves to
/// the FRONT face (Kazandu Mammoth, a Creature). The front-face builder already
/// attaches an <c>MdfcState</c> carrying a castable back-face
/// <c>MdfcFace.Land</c> descriptor (so <c>MdfcCastFlow</c> can offer "play as
/// the land Kazandu Valley" with its unconditional "this land enters tapped"
/// ETB — CR 614.1c) plus the landfall trigger. This arm simply delegates to
/// <see cref="KazanduMammothFactory.Create(Player)"/> so the cast-either-face +
/// landfall wiring is defined in exactly one place.
/// </summary>
[CardName("Kazandu Mammoth // Kazandu Valley")]
public static class KazanduMammothKazanduValleyFactory
{
    public const string CardName = "Kazandu Mammoth // Kazandu Valley";
    public const string Slug = "kazandu-mammoth-kazandu-valley";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Kazandu
    /// Mammoth, Creature — Elephant 3/3 {1}{G}{G}) with the <c>MdfcState</c>
    /// face tracker + castable back-face Land descriptor + landfall trigger
    /// attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + landfall
    /// behaviour come from <see cref="KazanduMammothFactory"/> so the
    /// cast-either-face logic lives in one place.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Combined-name overload forwarding a live <see cref="TriggerManager"/>
    /// to the front-face factory so the landfall trigger registers for firing.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity from the combined-name JSON (front-face characteristics).
        // The Load call validates the slug resource exists; the returned card
        // is then discarded in favour of the fully-wired front-face instance
        // below so the MDFC cast-either-face + landfall wiring is attached in
        // exactly one place (KazanduMammothFactory).
        _ = CardDefinitionLoader.FromEmbeddedResource(Slug);

        // Delegate to the front-face factory — it builds the Creature, attaches
        // the MdfcState with the castable back-face Land descriptor (CR 712.3 —
        // cast-either-face) and the landfall triggered ability.
        return KazanduMammothFactory.Create(owner, triggers);
    }
}
