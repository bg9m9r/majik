using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced LAND // LAND card
/// "Clearwater Pathway // Murkwater Pathway".
///
/// ## Why a separate combined-name arm
///
/// Both faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="ClearwaterPathwayFactory"/> — the FRONT face
///     (Land: "{T}: Add {U}."). It builds the land WITH its
///     <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> tracker (front =
///     "Clearwater Pathway", back = "Murkwater Pathway"), starting on the
///     front face.</item>
///   <item><see cref="MurkwaterPathwayFactory"/> — the BACK face
///     (Land: "{T}: Add {B}.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Clearwater Pathway // Murkwater Pathway") — that is the row whose
/// <c>IsImplemented</c> flag the engine derives from the <c>[CardName]</c>
/// registry (<see cref="ImplementedCardNames"/>). With only the two
/// single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the other combined-name MDFC factories do (e.g.
/// <see cref="SpikefieldHazardCombinedNameFactory"/> for
/// "Spikefield Hazard // Spikefield Cave"). The controller of an MDFC chooses
/// which face to play (CR 712.3); the front face is the default-existing one,
/// and it already carries the complete MDFC face tracker so the play-either-
/// face flow is preserved. No transform happens (CR 712.4 — MDFC faces don't
/// transform); the chosen face is simply the one that exists. Playing the back
/// face dispatches the back-face name to
/// <see cref="MurkwaterPathwayFactory"/> instead.
///
/// Delegating to the front-face factory keeps the combined arm a thin alias
/// over the already-tested wiring.
/// </summary>
[CardName("Clearwater Pathway // Murkwater Pathway")]
public static class ClearwaterPathwayMurkwaterPathwayCombinedFactory
{
    public const string CombinedName =
        "Clearwater Pathway // Murkwater Pathway";

    /// <summary>
    /// Build the front face (Clearwater Pathway — Land) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Clearwater Pathway, back = Murkwater Pathway). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches the combined printed name to.
    /// </summary>
    public static Land Create(Player owner) =>
        ClearwaterPathwayFactory.Create(owner);
}
