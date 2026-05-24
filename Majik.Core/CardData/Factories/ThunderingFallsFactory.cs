using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thundering Falls (Magic: The Gathering — Foundations).
///
/// U/R dual surveil land. Oracle text:
///   "This land enters tapped.
///    When this land enters, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)
///    {T}: Add {U} or {R}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/thundering-falls.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities + ETB-triggered surveil 1 are all JSON. Same Foundations
/// surveil-land cycle as <see cref="ElegantParlorFactory"/>,
/// <see cref="UndergroundMortuaryFactory"/> (Karlov's predecessor cycle).
///
/// ## Implemented elsewhere
/// - <b>Unconditional ETB-tapped</b>: handled at the binder layer
///   (<see cref="Majik.Core.CardData.EntersTappedBinder"/>) for the
///   production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>. This named-card
///   factory builds the land without the replacement (test convenience).
///
/// ## Deferred (v1 gaps)
/// - <b>Surveil decision player prompt</b>: agent-driven via
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseSurveilDecisionAsync"/>
///   when registered; otherwise default all-to-graveyard. Mirrors the
///   Underground Mortuary path.
/// </summary>
[CardName("Thundering Falls")]
public static class ThunderingFallsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("thundering-falls");

    /// <summary>
    /// Construct Thundering Falls owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
