using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Kaldheim modal
/// double-faced land "Brightclimb Pathway // Grimclimb Pathway".
///
/// ## Why a separate combined-name arm
///
/// The two faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="BrightclimbPathwayFactory"/> — the FRONT face
///     (Land: "{T}: Add {W}."). It builds the card WITH the
///     <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> tracker (front =
///     Brightclimb Pathway, back = Grimclimb Pathway) so the play-either-face
///     choice (CR 712.3) is observable from the front-face object.</item>
///   <item><see cref="GrimclimbPathwayFactory"/> — the BACK face
///     (Land: "{T}: Add {B}.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Brightclimb Pathway // Grimclimb Pathway") — that is the row whose
/// <c>IsImplemented</c> flag the engine derives from the <c>[CardName]</c>
/// registry (<see cref="ImplementedCardNames"/>). With only the two
/// single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the combined-name MDFC factories do (e.g.
/// <see cref="SpikefieldHazardCombinedNameFactory"/> for
/// "Spikefield Hazard // Spikefield Cave"). The front-face card already carries
/// the complete MDFC face tracker, so the play-either-face flow (CR 712.3 —
/// choose which face to play) is fully preserved. No transform happens
/// (CR 712.4 — MDFC faces don't transform); the chosen face is simply the one
/// that exists on the battlefield.
///
/// Delegating to the front-face factory keeps the combined arm a thin alias
/// over the already-tested wiring.
/// </summary>
[CardName("Brightclimb Pathway // Grimclimb Pathway")]
public static class BrightclimbPathwayCombinedNameFactory
{
    public const string CombinedName =
        "Brightclimb Pathway // Grimclimb Pathway";

    /// <summary>
    /// Build the front face (Brightclimb Pathway — Land) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Brightclimb Pathway, back = Grimclimb Pathway). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches the combined printed name to.
    /// </summary>
    public static Land Create(Player owner) =>
        BrightclimbPathwayFactory.Create(owner);
}
