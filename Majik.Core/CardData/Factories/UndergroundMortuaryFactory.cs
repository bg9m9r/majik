using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Underground Mortuary (Murders at Karlov Manor).
///
/// U/B dual surveil land. Oracle text:
///   "Underground Mortuary enters tapped unless you control two or more
///    other lands.
///    When Underground Mortuary enters untapped, surveil 1.
///    {T}: Add {U} or {B}."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/underground-mortuary.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities + ETB-triggered surveil 1 are all JSON.
///
/// ## Implemented elsewhere
/// - <b>ETB-tapped restriction</b>: "enters tapped unless you control two
///   or more other lands" is handled at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>) for
///   the production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>. This named-card
///   factory builds the land without the replacement (test convenience).
///
/// ## Deferred (v1 gaps)
/// - <b>"If it entered untapped" gate on surveil trigger</b>: the trigger
///   should only fire when the land entered untapped. v1 always fires
///   (no tapped-state tracking on ETB at trigger evaluation time).
/// - <b>Surveil decision player prompt</b>: agent-driven via
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseSurveilDecisionAsync"/>
///   when the owner's agent is registered in
///   <see cref="Majik.Core.Players.Agents.AgentRegistry"/>; falls back to
///   all-to-graveyard default when none is registered.
/// </summary>
public static class UndergroundMortuaryFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("underground-mortuary");

    /// <summary>
    /// Construct Underground Mortuary owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
