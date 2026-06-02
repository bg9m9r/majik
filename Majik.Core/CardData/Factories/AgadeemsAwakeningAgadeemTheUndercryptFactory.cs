using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card
/// <c>Agadeem's Awakening // Agadeem, the Undercrypt</c> ({X}{B}{B}{B}).
///
/// Oracle text (verified against Scryfall):
///   Front — Agadeem's Awakening, Sorcery {X}{B}{B}{B}:
///     "Return from your graveyard to the battlefield any number of target
///      creature cards that each have a different mana value X or less."
///   Back — Agadeem, the Undercrypt, Land:
///     "As this land enters, you may pay 3 life. If you don't, it enters
///      tapped." / "{T}: Add {B}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Agadeem's Awakening // Agadeem, the Undercrypt"). The two individual
/// faces are already implemented by <see cref="AgadeemsAwakeningFactory"/>
/// (front) and <see cref="AgadeemTheUndercryptFactory"/> (back), but the
/// engine's <c>IsImplemented</c> flag is keyed on the combined seed name —
/// so without a <c>[CardName]</c> registered for the COMBINED string the card
/// reads as unimplemented. This thin arm closes that gap, mirroring the
/// combined-name MDFC pattern used by <see cref="SinkIntoStuporFactory"/>
/// (cast-either-face via <see cref="MdfcFace.Land"/>) and
/// <see cref="BoomBustFactory"/> (combined name builds the front/castable
/// half).
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The
/// object that goes on the stack / is held in hand is the castable spell
/// half, so the combined name resolves to the FRONT face (Agadeem's
/// Awakening, a Sorcery). The front-face builder already attaches an
/// <see cref="MdfcState"/> carrying a castable back-face <see cref="MdfcFace.Land"/>
/// descriptor (so <c>MdfcCastFlow</c> can offer "play as the land Agadeem,
/// the Undercrypt" with its "pay 3 life or enter tapped" ETB — CR 614.1c).
/// This arm simply delegates to <see cref="AgadeemsAwakeningFactory.Create"/>
/// so the cast-either-face wiring is defined in exactly one place.
/// </summary>
[CardName("Agadeem's Awakening // Agadeem, the Undercrypt")]
public static class AgadeemsAwakeningAgadeemTheUndercryptFactory
{
    public const string CardName = "Agadeem's Awakening // Agadeem, the Undercrypt";
    public const string Slug = "agadeems-awakening-agadeem-the-undercrypt";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Agadeem's
    /// Awakening, Sorcery {X}{B}{B}{B}) with the <see cref="MdfcState"/> face
    /// tracker + castable back-face Land descriptor attached.
    ///
    /// Identity is loaded from the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + resolve-time
    /// spell behaviour come from <see cref="AgadeemsAwakeningFactory"/> so the
    /// cast-either-face logic lives in one place.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity from the combined-name JSON (front-face characteristics).
        // The Build call validates the slug resource exists / matches the
        // front face; the returned card is then discarded in favour of the
        // fully-wired front-face instance below so the MDFC cast-either-face
        // descriptor is attached in exactly one place.
        _ = CardDefinitionLoader.FromEmbeddedResource(Slug);

        // Delegate to the front-face factory — it builds the Sorcery and
        // attaches the MdfcState with the castable back-face Land descriptor
        // (CR 712.3 — cast-either-face).
        return AgadeemsAwakeningFactory.Create(owner);
    }
}
