using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card <c>Hagra Mauling // Hagra Broodpit</c> ({2}{B}{B}).
///
/// Oracle text (verified against Scryfall):
///   Front — Hagra Mauling, Instant {2}{B}{B}:
///     "This spell costs {1} less to cast if an opponent controls no basic
///      lands.
///      Destroy target creature."
///   Back — Hagra Broodpit, Land:
///     "This land enters tapped." / "{T}: Add {B}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Hagra Mauling // Hagra Broodpit"). The two individual faces are already
/// implemented by <see cref="HagraMaulingFactory"/> (front) and
/// <see cref="HagraBroodpitFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by
/// <see cref="ValakutAwakeningValakutStoneforgeFactory"/> and
/// <see cref="SilundiVisionSilundiIsleFactory"/> (combined name builds the
/// castable front/spell half).
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand is the castable spell half, so the
/// combined name resolves to the FRONT face (Hagra Mauling, an Instant). The
/// front-face builder already attaches an <see cref="MdfcState"/> carrying a
/// castable back-face <see cref="MdfcFace.Land"/> descriptor (so
/// <c>MdfcCastFlow</c> can offer "play as the land Hagra Broodpit" with its
/// unconditional "this land enters tapped" ETB — CR 614.1c), and it carries the
/// opponent-board-aware {1} cost reduction (CR 117.7 — "costs {1} less if an
/// opponent controls no basic lands"). This arm simply delegates to
/// <see cref="HagraMaulingFactory.Create"/> so the cast-either-face wiring,
/// cost reducer, and "destroy target creature" resolve behaviour are defined in
/// exactly one place.
/// </summary>
[CardName("Hagra Mauling // Hagra Broodpit")]
public static class HagraMaulingHagraBroodpitFactory
{
    public const string CardName = "Hagra Mauling // Hagra Broodpit";
    public const string Slug = "hagra-mauling-hagra-broodpit";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Hagra Mauling,
    /// Instant {2}{B}{B}) with the <see cref="MdfcState"/> face tracker +
    /// castable back-face Land descriptor + opponent-board-aware cost reducer
    /// attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring, cost reducer,
    /// and resolve-time "destroy target creature" behaviour come from
    /// <see cref="HagraMaulingFactory"/> so the cast-either-face logic lives in
    /// one place.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity from the combined-name JSON (front-face characteristics).
        // The Load call validates the combined-slug resource exists / matches
        // the front face; the returned definition is then discarded in favour
        // of the fully-wired front-face instance below so the MDFC
        // cast-either-face descriptor + cost reducer are attached in exactly
        // one place.
        _ = CardDefinitionLoader.FromEmbeddedResource(Slug);

        // Delegate to the front-face factory — it builds the Instant, attaches
        // the MdfcState with the castable back-face Land descriptor (CR 712.3 —
        // cast-either-face), the opponent-board-aware {1} cost reduction
        // (CR 117.7), and the "destroy target creature" resolve definition.
        return HagraMaulingFactory.Create(owner);
    }
}
