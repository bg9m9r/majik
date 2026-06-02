using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED printed name of the Zendikar Rising
/// modal double-faced card
/// "Shatterskull Smashing // Shatterskull, the Hammer Pass" ({X}{R}{R}).
///
/// ## Why a separate combined-name arm
///
/// The two faces of this MDFC are already implemented as independent
/// <c>[CardName]</c>-dispatched factories:
/// <list type="bullet">
///   <item><see cref="ShatterskullSmashingFactory"/> — the FRONT face
///     (Sorcery, {X}{R}{R}): "deals X damage divided among up to two target
///     creatures and/or planeswalkers; twice X if X is 6 or more". It builds
///     the card WITH the full <see cref="Majik.Core.CardData.MDFCs.MdfcState"/>
///     wiring, including a castable back-face land descriptor so
///     <see cref="Majik.Core.Game.MdfcCastFlow"/> can offer the
///     cast-either-face choice (CR 712.3 / 712.4).</item>
///   <item><see cref="ShatterskullTheHammerPassFactory"/> — the BACK face
///     (Land: "{T}: Add {R}"; "As this land enters, you may pay 3 life. If
///     you don't, it enters tapped.").</item>
/// </list>
///
/// However, the embedded Modern seed keys this card under its <b>combined</b>
/// name ("Shatterskull Smashing // Shatterskull, the Hammer Pass") — that is
/// the row whose <c>IsImplemented</c> flag the engine derives from the
/// <c>[CardName]</c> registry (<see cref="ImplementedCardNames"/>). With only
/// the two single-face names registered, the combined seed row stays
/// <c>IsImplemented=false</c> and any deck referencing the combined name fails
/// to dispatch.
///
/// This arm registers the combined name and dispatches it to the FRONT face,
/// exactly as the combined-name MDFC factories do (e.g.
/// <see cref="RalMonsoonMageFactory"/> for
/// "Ral, Monsoon Mage // Ral, Leyline Prodigy"). The front-face card already
/// carries the complete MDFC face tracker + castable back-face land
/// descriptor, so the cast-either-face flow (CR 712.3 — choose which face to
/// cast/play) is fully preserved. No transform happens (CR 712.4 — MDFC faces
/// don't transform); the chosen face is simply the one that exists.
///
/// The JSON-definition load path
/// (<c>CardDefinitionLoader.FromEmbeddedResource</c> +
/// <c>CardDefinitionFactory.Build</c>) is intentionally NOT used here: it
/// produces a single-typed card from declarative ability data and cannot
/// express the dual-faced sorcery-or-land cast-either-face shape, which is why
/// every MDFC pair in this codebase is code-built rather than JSON-built.
/// Delegating to the front-face factory keeps the combined arm a thin alias
/// over the already-tested wiring.
/// </summary>
[CardName("Shatterskull Smashing // Shatterskull, the Hammer Pass")]
public static class ShatterskullSmashingCombinedNameFactory
{
    public const string CombinedName =
        "Shatterskull Smashing // Shatterskull, the Hammer Pass";

    /// <summary>
    /// Build the front face (Shatterskull Smashing — Sorcery) with its full
    /// <see cref="Majik.Core.CardData.MDFCs.MdfcState"/> wiring (front =
    /// Shatterskull Smashing, castable back = the land Shatterskull, the
    /// Hammer Pass). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches the combined printed name to.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        ShatterskullSmashingFactory.Create(owner);
}
