using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Kaldheim
/// modal double-faced land
/// "Blightstep Pathway // Searstep Pathway".
///
/// ## Why a separate combined-name arm
///
/// The two faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="BlightstepPathwayFactory"/> — the FRONT face
///     (Land: "{T}: Add {B}."). It builds the land WITH the full
///     <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
///     Blightstep Pathway, back = the land Searstep Pathway), starting on the
///     front face.</item>
///   <item><see cref="SearstepPathwayFactory"/> — the BACK face
///     (Land: "{T}: Add {R}.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Blightstep Pathway // Searstep Pathway") — that is the row whose
/// <c>IsImplemented</c> flag the engine derives from the <c>[CardName]</c>
/// registry (<see cref="ImplementedCardNames"/>). With only the two
/// single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the combined-name MDFC factories do (e.g.
/// <see cref="HengegatePathwayCombinedNameFactory"/> for the Kaldheim
/// land//land "Hengegate Pathway // Mistgate Pathway", or
/// <see cref="SpikefieldHazardCombinedNameFactory"/> for the Zendikar Rising
/// instant//land "Spikefield Hazard // Spikefield Cave"). The front-face land
/// already carries the complete MDFC face tracker, so the play-either-face
/// flow (CR 712.3 — choose which face to cast/play) is fully preserved. No
/// transform happens (CR 712.4 — MDFC faces don't transform); the chosen face
/// is simply the one that exists.
///
/// Delegating to the front-face factory keeps the combined arm a thin alias
/// over the already-tested wiring (JSON def + <c>CardDefinitionFactory</c>
/// build + <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> attach).
/// </summary>
[CardName("Blightstep Pathway // Searstep Pathway")]
public static class BlightstepPathwayCombinedNameFactory
{
    public const string CombinedName =
        "Blightstep Pathway // Searstep Pathway";

    /// <summary>
    /// Build the front face (Blightstep Pathway — Land) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Blightstep Pathway, back = Searstep Pathway). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches the combined printed name to.
    /// </summary>
    public static Land Create(Player owner) =>
        BlightstepPathwayFactory.Create(owner);
}
