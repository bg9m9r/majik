using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced "Pathway" dual land
/// "Branchloft Pathway // Boulderloft Pathway".
///
/// ## Why a separate combined-name arm
///
/// The two faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="BranchloftPathwayFactory"/> — the FRONT face
///     (Land: "{T}: Add {G}."). It builds the card WITH the full
///     <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring
///     (front = Branchloft Pathway, back = Boulderloft Pathway).</item>
///   <item><see cref="BoulderloftPathwayFactory"/> — the BACK face
///     (Land: "{T}: Add {W}.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Branchloft Pathway // Boulderloft Pathway") — that is the row whose
/// <c>IsImplemented</c> flag the engine derives from the <c>[CardName]</c>
/// registry (<see cref="ImplementedCardNames"/>). With only the two
/// single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the other combined-name MDFC factories do (e.g.
/// <see cref="SpikefieldHazardCombinedNameFactory"/> for
/// "Spikefield Hazard // Spikefield Cave", or
/// <see cref="ShatterskullSmashingCombinedNameFactory"/> for
/// "Shatterskull Smashing // Shatterskull, the Hammer Pass"). The front-face
/// card already carries the complete MDFC face tracker, so the
/// play-either-face flow (CR 712.6 / 305.1 — choose which face to put onto the
/// battlefield) is fully preserved. No transform happens (CR 712.4 — MDFC faces
/// don't transform); the chosen face is simply the one that exists.
///
/// Delegating to <see cref="BranchloftPathwayFactory.Create"/> keeps the
/// combined arm a thin alias over the already-tested front-face wiring (JSON
/// identity + {T}: Add {G} mana ability + attached <c>MdfcState</c>).
/// </summary>
[CardName("Branchloft Pathway // Boulderloft Pathway")]
public static class BranchloftPathwayBoulderloftPathwayCombinedFactory
{
    public const string CombinedName =
        "Branchloft Pathway // Boulderloft Pathway";

    /// <summary>
    /// Build the front face (Branchloft Pathway — Land) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Branchloft Pathway, back = Boulderloft Pathway). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches the combined printed name to.
    /// </summary>
    public static Land Create(Player owner) =>
        BranchloftPathwayFactory.Create(owner);
}
