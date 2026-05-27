using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Meticulous Archive (Murders at Karlov Manor /
/// Foundations).
///
/// W/U dual surveil land. Oracle text:
///   "This land enters tapped.
///    When this land enters, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)
///    {T}: Add {W} or {U}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/meticulous-archive.json</c>; same surveil-land
/// cycle as <see cref="ElegantParlorFactory"/>. Unconditional ETB-tapped is
/// applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience). Surveil decision is
/// agent-driven via
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseSurveilDecisionAsync"/>
/// when registered, otherwise default all-to-graveyard.
/// </summary>
[CardName("Meticulous Archive")]
public static class MeticulousArchiveFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("meticulous-archive");

    /// <summary>Construct Meticulous Archive owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
