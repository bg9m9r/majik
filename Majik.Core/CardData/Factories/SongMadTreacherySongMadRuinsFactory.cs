using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Kamigawa: Neon
/// Dynasty modal double-faced card <c>Song-Mad Treachery // Song-Mad Ruins</c>
/// ({3}{R}{R}).
///
/// Oracle text (verified against the implemented single faces + Scryfall):
///   Front — Song-Mad Treachery, Sorcery {3}{R}{R}:
///     "Gain control of target creature until end of turn. Untap that
///      creature. It gains haste until end of turn." (Threaten template).
///   Back — Song-Mad Ruins, Land:
///     "This land enters tapped." / "{T}: Add {R}."
///
/// ## Why a combined-name arm
///
/// The embedded Modern seed lists this card under its combined printed name
/// ("Song-Mad Treachery // Song-Mad Ruins"). The two individual faces are
/// already implemented by <see cref="SongMadTreacheryFactory"/> (front) and
/// <see cref="SongMadRuinsFactory"/> (back), but the engine's
/// <c>IsImplemented</c> flag is keyed on the combined seed name — so without a
/// <c>[CardName]</c> registered for the COMBINED string the card reads as
/// unimplemented. This thin arm closes that gap, mirroring the combined-name
/// MDFC pattern used by
/// <see cref="SilundiVisionSilundiIsleFactory"/> /
/// <see cref="AgadeemsAwakeningAgadeemTheUndercryptFactory"/>.
///
/// ## What it builds
///
/// CR 712.3 — when a player casts/plays an MDFC they choose a face. The object
/// that goes on the stack / is held in hand is the castable spell half, so the
/// combined name resolves to the FRONT face (Song-Mad Treachery, a Sorcery).
/// The front-face builder already attaches an <c>MdfcState</c> carrying a
/// castable back-face Land descriptor (so <c>MdfcCastFlow</c> can offer "play
/// as the land Song-Mad Ruins" with its unconditional "this land enters
/// tapped" ETB — CR 614.1c). This arm simply delegates to
/// <see cref="SongMadTreacheryFactory.Create"/> so the cast-either-face wiring
/// is defined in exactly one place.
/// </summary>
[CardName("Song-Mad Treachery // Song-Mad Ruins")]
public static class SongMadTreacherySongMadRuinsFactory
{
    public const string CardName = "Song-Mad Treachery // Song-Mad Ruins";
    public const string Slug = "song-mad-treachery-song-mad-ruins";

    /// <summary>
    /// Build the combined-name MDFC as its castable FRONT face (Song-Mad
    /// Treachery, Sorcery {3}{R}{R}) with the <c>MdfcState</c> face tracker +
    /// castable back-face Land descriptor attached.
    ///
    /// Identity is validated against the embedded JSON definition keyed on the
    /// combined slug (<see cref="Slug"/>); the MDFC face wiring + resolve-time
    /// gain-control behaviour come from <see cref="SongMadTreacheryFactory"/>
    /// so the cast-either-face logic lives in one place.
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
        return SongMadTreacheryFactory.Create(owner);
    }
}
