using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Wilds of Eldraine
/// modal double-faced card <c>Witch Enchanter // Witch-Blessed Meadow</c>
/// ({3}{W}).
///
/// Oracle text (verified against Scryfall):
///   Front — Witch Enchanter, Creature — Human Warlock 2/2:
///     "When this creature enters, destroy target artifact or enchantment an
///      opponent controls."
///   Back — Witch-Blessed Meadow, Land:
///     "As this land enters, you may pay 3 life. If you don't, it enters
///      tapped." / "{T}: Add {W}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Witch Enchanter // Witch-Blessed Meadow"). The two individual faces are
/// already implemented by <see cref="WitchEnchanterFactory"/> (front) and
/// <see cref="WitchBlessedMeadowFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by <see cref="SilundiVisionSilundiIsleFactory"/> /
/// <see cref="SinkIntoStuporFactory"/>.
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand for the spell half is the castable
/// front face, so the combined name resolves to the FRONT face (Witch
/// Enchanter, a Creature). This arm delegates to
/// <see cref="WitchEnchanterFactory.Create(Player)"/> so the front-face ETB
/// destroy trigger + the <c>MdfcState</c> face tracker (front = "Witch
/// Enchanter", back = "Witch-Blessed Meadow") are wired in exactly one place.
/// The back-face land (CR 614.1c "pay 3 life or enter tapped") is played via
/// <see cref="WitchBlessedMeadowFactory"/> on the back-face name dispatch arm.
/// </summary>
[CardName("Witch Enchanter // Witch-Blessed Meadow")]
public static class WitchEnchanterWitchBlessedMeadowFactory
{
    public const string CardName = "Witch Enchanter // Witch-Blessed Meadow";
    public const string Slug = "witch-enchanter-witch-blessed-meadow";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Witch
    /// Enchanter, Creature — Human Warlock {3}{W} 2/2) with the
    /// <see cref="MDFCs.MdfcState"/> face tracker + ETB destroy trigger
    /// attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + the ETB
    /// destroy trigger come from <see cref="WitchEnchanterFactory"/> so the
    /// front-face logic lives in one place.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity from the combined-name JSON (front-face characteristics).
        // The Load call validates the slug resource exists; the returned
        // definition is then discarded in favour of the fully-wired front-face
        // instance below so the MDFC face tracker + ETB trigger are attached in
        // exactly one place.
        _ = CardDefinitionLoader.FromEmbeddedResource(Slug);

        // Delegate to the front-face factory — it builds the Creature, attaches
        // the MdfcState (front = "Witch Enchanter", back = "Witch-Blessed
        // Meadow"), and wires the ETB "destroy target artifact or enchantment
        // an opponent controls" trigger (CR 603.6a).
        return WitchEnchanterFactory.Create(owner);
    }
}
