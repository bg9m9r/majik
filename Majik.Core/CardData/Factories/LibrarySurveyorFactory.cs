using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Library Surveyor — synthetic Surveil keyword
/// fixture (no real Modern-legal printed card had a cleanly-isolated
/// "Surveil N" effect without scope-creep beyond the v1 keyword pipeline).
///
/// Creature — Human Wizard {1}{U} 1/2. Oracle text:
///   "When Library Surveyor enters, surveil 2."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/library-surveyor.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Single
/// ETB-triggered <c>surveil_self</c> ability — JSON only.
///
/// ## Implemented (v1)
/// - Vanilla 1/2 Human Wizard shell with no Modern-relevant rider.
/// - ETB trigger: Surveil 2 (CR 701.42). Decision routes through the
///   registered <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>; falls
///   back to all-to-graveyard when no agent is registered. Mirrors the
///   Underground Mortuary / Thundering Falls surveil-land path.
///
/// ## Deferred
/// - Surveil decision player prompt comes from
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseSurveilDecisionAsync"/>
///   when an agent is registered; otherwise the deterministic default applies.
/// </summary>
public static class LibrarySurveyorFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("library-surveyor");

    /// <summary>
    /// Construct Library Surveyor owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
