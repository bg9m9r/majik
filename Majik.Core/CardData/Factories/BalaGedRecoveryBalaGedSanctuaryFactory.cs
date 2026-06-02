using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card
/// <c>Bala Ged Recovery // Bala Ged Sanctuary</c> ({2}{G}).
///
/// Oracle text (verified against Scryfall):
///   Front — Bala Ged Recovery, Sorcery {2}{G}:
///     "Return target card from your graveyard to your hand."
///   Back — Bala Ged Sanctuary, Land:
///     "This land enters tapped." / "{T}: Add {G}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Bala Ged Recovery // Bala Ged Sanctuary"). The two individual faces are
/// already implemented by <see cref="BalaGedRecoveryFactory"/> (front) and
/// <see cref="BalaGedSanctuaryFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by
/// <see cref="AgadeemsAwakeningAgadeemTheUndercryptFactory"/> and
/// <see cref="SeaGateRestorationSeaGateRebornFactory"/> (combined name builds
/// the front/castable half).
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand is the castable spell half, so the
/// combined name resolves to the FRONT face (Bala Ged Recovery, a Sorcery).
/// The front-face builder already attaches an <see cref="MdfcState"/> carrying
/// a castable back-face <see cref="MdfcFace.Land"/> descriptor (so
/// <c>MdfcCastFlow</c> can offer "play as the land Bala Ged Sanctuary" with its
/// unconditional "enters tapped" ETB — CR 614.1c). This arm simply delegates to
/// <see cref="BalaGedRecoveryFactory.Create"/> so the cast-either-face wiring is
/// defined in exactly one place.
/// </summary>
[CardName("Bala Ged Recovery // Bala Ged Sanctuary")]
public static class BalaGedRecoveryBalaGedSanctuaryFactory
{
    public const string CardName = "Bala Ged Recovery // Bala Ged Sanctuary";
    public const string Slug = "bala-ged-recovery-bala-ged-sanctuary";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Bala Ged
    /// Recovery, Sorcery {2}{G}) with the <see cref="MdfcState"/> face tracker
    /// + castable back-face Land descriptor attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + resolve-time
    /// spell behaviour come from <see cref="BalaGedRecoveryFactory"/> so the
    /// cast-either-face logic lives in one place.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity from the combined-name JSON (front-face characteristics).
        // The Build call validates the slug resource exists / matches the
        // front face; the returned definition is then discarded in favour of
        // the fully-wired front-face instance below so the MDFC
        // cast-either-face descriptor is attached in exactly one place.
        _ = CardDefinitionLoader.FromEmbeddedResource(Slug);

        // Delegate to the front-face factory — it builds the Sorcery and
        // attaches the MdfcState with the castable back-face Land descriptor
        // (CR 712.3 — cast-either-face).
        return BalaGedRecoveryFactory.Create(owner);
    }
}
