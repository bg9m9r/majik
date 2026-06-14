using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boseiju, Who Endures (Kamigawa: Neon Dynasty).
///
/// Legendary Land.
/// Oracle text:
///   "Boseiju, Who Endures enters tapped unless you control two or fewer
///    other lands.
///    {T}: Add {G}.
///    Channel — {1}{G}, Discard Boseiju, Who Endures: Destroy target
///    artifact, enchantment, or nonbasic land an opponent controls. If that
///    permanent was a land, its controller may search their library for a
///    basic land card, put it onto the battlefield, then shuffle."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/boseiju.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The
/// Channel activated ability is fully JSON: {1}{G} + discard-self →
/// destroy-target stub.
///
/// ## Implemented elsewhere
/// - <b>ETB-tapped restriction</b>: "enters tapped unless you control
///   two or fewer other lands" is handled at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>) for
///   the production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>. This named-card
///   factory builds the land without the replacement (test convenience).
///
/// ## Implemented (PLAN 01 Slice F)
/// - <b>Channel effect — target selection + actual destroy</b>: the
///   activated ability emits a real <c>destroy_target</c> effect declaring a
///   1..1 target request over artifact / enchantment / nonbasic-land
///   candidates. The shared <see cref="Majik.Core.Targeting.TargetCollection"/>
///   pipeline prompts the controller's agent, and the effect destroys the
///   chosen permanent via
///   <see cref="Majik.Core.Primitives.Fx.MoveToGraveyard(Majik.Core.Cards.ICard, Majik.Core.Zones.ZoneMoveReason)"/>
///   (CR 701.7 / 608.2b — Indestructible / regeneration gated).
///
/// ## Implemented (deferral pay-down: boseiju-channel-opponent-land-search)
/// - <b>Channel effect — "that player may search" follow-up</b>: after the
///   destroy resolves, the destroyed permanent's controller (the OTHER
///   player) "may search their library for a land card with a basic land
///   type, put it onto the battlefield, then shuffle" (CR 701.19a /
///   CR 701.20a / CR 205.4a). Expressed declaratively as the
///   <c>thenControllerMaySearch</c> rider on the JSON
///   <see cref="Majik.Core.CardData.Definitions.DestroyTargetEffectDef"/>,
///   which nests the shared <c>search_library</c> tutor verb. The runtime
///   binds the search prompt to the affected player's OWN agent
///   (<see cref="Majik.Core.Players.Agents.AgentRegistry"/>) — the Path to
///   Exile per-affected-player pattern — so they search their own library.
///   The subtype filter (the five basic land types) keys on the basic land
///   TYPE, so a nonbasic dual with a basic land type qualifies (CR 205.4a);
///   the destination is the battlefield UNTAPPED (no "tapped" rider in the
///   current oracle).
///
/// ## Deferred (v1 gaps)
/// - <b>Channel cost reduction</b>: "This ability costs {1} less to activate
///   for each legendary creature you control" is NOT yet modelled — the JSON
///   fixes the activation cost at {1}{G}. Out of scope for this deferral
///   (the named gap is the opponent land-search follow-up).
/// </summary>
[CardName("Boseiju, Who Endures")]
public static class BoseijuFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("boseiju");

    /// <summary>
    /// Construct Boseiju with no target/opponent resolver (test /
    /// vanilla path). The Channel destroy effect is a no-op in this
    /// mode (stub effect — see class xmldoc).
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);

    /// <summary>
    /// Legacy overload kept for callers that previously passed an
    /// <paramref name="opponentsResolver"/>. The JSON-driven factory
    /// doesn't need it — the destroy effect is a stub closure that
    /// resolves to a no-op regardless. Kept so the public API doesn't
    /// break compatibility; the resolver argument is intentionally
    /// ignored.
    /// </summary>
    public static Land Create(Player owner, Func<IReadOnlyList<Player>>? opponentsResolver)
    {
        _ = opponentsResolver;
        return Create(owner);
    }
}
