using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory registered under the COMBINED printed name of the
/// Zendikar Rising modal double-faced card
/// <c>Sea Gate Restoration // Sea Gate, Reborn</c> ({4}{U}{U}{U}).
///
/// ## Why a combined-name arm is needed
///
/// The embedded Modern seed keys every MDFC under its printed COMBINED name
/// (Scryfall's <c>name</c> field), e.g. the row is literally
/// <c>"Sea Gate Restoration // Sea Gate, Reborn"</c>. The two single-face
/// factories, however, register only their individual face names —
/// <see cref="SeaGateRestorationFactory"/> = <c>"Sea Gate Restoration"</c>
/// and <see cref="SeaGateRebornFactory"/> = <c>"Sea Gate, Reborn"</c>.
/// <see cref="NamedCardFactory.Create"/> dispatches on an EXACT name match,
/// so without a factory carrying the combined name the seed row falls through
/// to the vanilla-shell fallback and <c>IsImplemented</c> never flips on for
/// the real card.
///
/// This factory closes that gap. Dispatching the combined name builds the
/// FRONT face — the Sorcery — exactly as <see cref="SeaGateRestorationFactory"/>
/// does, including the <see cref="MdfcState"/> back-face-land descriptor (the
/// back face is the LAND Sea Gate, Reborn). At cast time
/// <see cref="MdfcCastFlow"/> consults that descriptor to offer the controller
/// a face choice (CR 712.3 / 712.4 — cast either face; no transform happens).
///
/// The mirror of the single-face front factory — see
/// <see cref="SinkIntoStuporFactory"/> for the same combined-name MDFC posture
/// (back-face land via <see cref="MdfcFace.Land"/>).
///
/// ## Card identity + resolve come from the single-face front factory
///
/// To build the front face this factory delegates to
/// <see cref="SeaGateRestorationFactory.Create"/>, which loads the front-face
/// identity from the embedded JSON definition (<c>sea-gate-restoration.json</c>:
/// name "Sea Gate Restoration", Sorcery, {4}{U}{U}{U}) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>, and attaches the same
/// <see cref="MdfcState"/> back-face-land descriptor. The built front-face card
/// object is therefore named "Sea Gate Restoration" even though the dispatch
/// KEY is the combined printed name.
///
/// Delegating (rather than re-declaring the same identity in a second JSON)
/// avoids a duplicate <c>name</c> collision — the source generator's MJK003
/// guard requires each JSON <c>name</c> to map to a single embedded resource —
/// and keeps the resolve-time draw effect ("draw cards equal to the number of
/// cards in your hand plus one") and the no-op "no maximum hand size" rider in
/// exactly one place (<see cref="SeaGateRestorationFactory.BuildSpellDefinition"/>).
/// </summary>
[CardName("Sea Gate Restoration // Sea Gate, Reborn")]
public static class SeaGateRestorationSeaGateRebornFactory
{
    public const string CombinedName = "Sea Gate Restoration // Sea Gate, Reborn";
    public const string FrontName = "Sea Gate Restoration";
    public const string BackName = "Sea Gate, Reborn";

    /// <summary>
    /// Construct the front face (Sea Gate Restoration, a Sorcery) by delegating
    /// to <see cref="SeaGateRestorationFactory.Create"/> — which loads the
    /// front-face identity from JSON and attaches the <see cref="MdfcState"/>
    /// back-face land descriptor (back face = the land Sea Gate, Reborn). The
    /// resolve-time draw <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="SeaGateRestorationFactory.BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 712.3 / 712.4 — the combined printed name resolves to the FRONT
        // face. Reuse the single-face front factory verbatim so identity,
        // MdfcState back-face-land wiring, and the resolve-time draw all stay
        // in one place.
        return SeaGateRestorationFactory.Create(owner);
    }
}
