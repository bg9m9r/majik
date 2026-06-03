using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Kaldheim land //
/// land modal double-faced card "Darkbore Pathway // Slitherbore Pathway".
///
/// ## Why a separate combined-name arm
///
/// The two faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="DarkborePathwayFactory"/> — the FRONT face
///     (Land: "{T}: Add {B}."). It builds the card WITH the full
///     <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> face tracker
///     (front = Darkbore Pathway, back = Slitherbore Pathway), so the
///     play-either-face choice (CR 712.10 — choose which face to play) is
///     preserved.</item>
///   <item><see cref="SlitherborePathwayFactory"/> — the BACK face
///     (Land: "{T}: Add {G}.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Darkbore Pathway // Slitherbore Pathway") — that is the row whose
/// <c>IsImplemented</c> flag the engine derives from the <c>[CardName]</c>
/// registry (<see cref="ImplementedCardNames"/>). With only the two
/// single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the other combined-name MDFC factories do (e.g.
/// <see cref="SpikefieldHazardCombinedNameFactory"/> for
/// "Spikefield Hazard // Spikefield Cave"). The front-face card already
/// carries the complete MDFC face tracker, so the play-either-face flow
/// (CR 712.10) is fully preserved. No transform happens (CR 712.4 — MDFC faces
/// don't transform); the chosen face is simply the one that exists.
///
/// Delegating to the front-face factory keeps the combined arm a thin alias
/// over the already-tested wiring — the JSON-backed identity + the
/// <c>{T}: Add {B}</c> mana ability (CR 605.1) come from
/// <see cref="DarkborePathwayFactory"/>.
/// </summary>
[CardName("Darkbore Pathway // Slitherbore Pathway")]
public static class DarkborePathwaySlitherborePathwayCombinedNameFactory
{
    public const string CombinedName =
        "Darkbore Pathway // Slitherbore Pathway";

    /// <summary>
    /// Build the front face (Darkbore Pathway — Land) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Darkbore Pathway, back = Slitherbore Pathway). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches the combined printed name to.
    /// </summary>
    public static Land Create(Player owner) =>
        DarkborePathwayFactory.Create(owner);
}
