using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced land
/// "Riverglide Pathway // Lavaglide Pathway".
///
/// ## Why a separate combined-name arm
///
/// The two faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="RiverglidePathwayFactory"/> — the FRONT face
///     (Land: "{T}: Add {U}."). It builds the land WITH the full
///     <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
///     Riverglide Pathway, back = the land Lavaglide Pathway), starting on the
///     front face.</item>
///   <item><see cref="LavaglidePathwayFactory"/> — the BACK face
///     (Land: "{T}: Add {R}.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Riverglide Pathway // Lavaglide Pathway") — that is the row whose
/// <c>IsImplemented</c> flag the engine derives from the <c>[CardName]</c>
/// registry (<see cref="ImplementedCardNames"/>). With only the two
/// single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the combined-name MDFC factories do (e.g.
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
[CardName("Riverglide Pathway // Lavaglide Pathway")]
public static class RiverglidePathwayCombinedNameFactory
{
    public const string CombinedName =
        "Riverglide Pathway // Lavaglide Pathway";

    /// <summary>
    /// Build the front face (Riverglide Pathway — Land) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Riverglide Pathway, back = Lavaglide Pathway). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches the combined printed name to.
    /// </summary>
    public static Land Create(Player owner) =>
        RiverglidePathwayFactory.Create(owner);
}
