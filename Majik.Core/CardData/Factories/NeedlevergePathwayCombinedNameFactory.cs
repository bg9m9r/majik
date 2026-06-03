using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Streets of New
/// Capenna modal double-faced land
/// "Needleverge Pathway // Pillarverge Pathway".
///
/// ## Why a separate combined-name arm
///
/// The two faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="NeedlevergePathwayFactory"/> — the FRONT face
///     (Land: "{T}: Add {R}."). It builds the land WITH the full
///     <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
///     Needleverge Pathway, back = the land Pillarverge Pathway), starting on
///     the front face.</item>
///   <item><see cref="PillarvergePathwayFactory"/> — the BACK face
///     (Land: "{T}: Add {W}.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Needleverge Pathway // Pillarverge Pathway") — that is the row whose
/// <c>IsImplemented</c> flag the engine derives from the <c>[CardName]</c>
/// registry (<see cref="ImplementedCardNames"/>). With only the two
/// single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the combined-name MDFC factories do (e.g.
/// <see cref="RiverglidePathwayCombinedNameFactory"/> for the Zendikar Rising
/// land//land "Riverglide Pathway // Lavaglide Pathway"). The front-face land
/// already carries the complete MDFC face tracker, so the play-either-face
/// flow (CR 712.3 — choose which face to cast/play) is fully preserved. No
/// transform happens (CR 712.4 — MDFC faces don't transform); the chosen face
/// is simply the one that exists.
///
/// Delegating to the front-face factory keeps the combined arm a thin alias
/// over the already-tested wiring (JSON def + <c>CardDefinitionFactory</c>
/// build + <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> attach).
/// </summary>
[CardName("Needleverge Pathway // Pillarverge Pathway")]
public static class NeedlevergePathwayCombinedNameFactory
{
    public const string CombinedName =
        "Needleverge Pathway // Pillarverge Pathway";

    /// <summary>
    /// Build the front face (Needleverge Pathway — Land) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Needleverge Pathway, back = Pillarverge Pathway). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches the combined printed name to.
    /// </summary>
    public static Land Create(Player owner) =>
        NeedlevergePathwayFactory.Create(owner);
}
