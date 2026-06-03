using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Kaldheim modal
/// double-faced "Pathway" dual land
/// "Cragcrown Pathway // Timbercrown Pathway".
///
/// ## Why a separate combined-name arm
///
/// The two faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="CragcrownPathwayFactory"/> — the FRONT face
///     (Land: "{T}: Add {R}."). It builds the card WITH the full
///     <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring
///     (front = Cragcrown Pathway, back = Timbercrown Pathway).</item>
///   <item><see cref="TimbercrownPathwayFactory"/> — the BACK face
///     (Land: "{T}: Add {G}.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Cragcrown Pathway // Timbercrown Pathway") — that is the row whose
/// <c>IsImplemented</c> flag the engine derives from the <c>[CardName]</c>
/// registry (<see cref="ImplementedCardNames"/>). With only the two
/// single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the other combined-name MDFC factories do (e.g.
/// <see cref="BranchloftPathwayBoulderloftPathwayCombinedFactory"/> for
/// "Branchloft Pathway // Boulderloft Pathway", or
/// <see cref="SpikefieldHazardCombinedNameFactory"/> for
/// "Spikefield Hazard // Spikefield Cave"). The front-face card already carries
/// the complete MDFC face tracker, so the play-either-face flow
/// (CR 712.6 / 305.1 — choose which face to put onto the battlefield) is fully
/// preserved. No transform happens (CR 712.4 — MDFC faces don't transform); the
/// chosen face is simply the one that exists.
///
/// Delegating to <see cref="CragcrownPathwayFactory.Create"/> keeps the
/// combined arm a thin alias over the already-tested front-face wiring (JSON
/// identity + {T}: Add {R} mana ability + attached <c>MdfcState</c>).
/// </summary>
[CardName("Cragcrown Pathway // Timbercrown Pathway")]
public static class CragcrownPathwayTimbercrownPathwayCombinedFactory
{
    public const string CombinedName =
        "Cragcrown Pathway // Timbercrown Pathway";

    /// <summary>
    /// Build the front face (Cragcrown Pathway — Land) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Cragcrown Pathway, back = Timbercrown Pathway). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches the combined printed name to.
    /// </summary>
    public static Land Create(Player owner) =>
        CragcrownPathwayFactory.Create(owner);
}
