using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Epiphany (Theros Beyond Death).
///
/// U/R "scry land". Oracle text:
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {U} or {R}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/temple-of-epiphany.json</c>. Same oracle
/// shape as <see cref="TempleOfTriumphFactory"/> (the Theros scry-land cycle),
/// only the two single-colour mana abilities produce {U} and {R}. The ETB
/// keyword action is scry 1 (CR 701.20). Unconditional ETB-tapped
/// (CR 614.1c) is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience — matches the scry-land posture).
/// Scry decision is agent-driven via
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseScryDecisionAsync"/>
/// when registered, otherwise default all-to-bottom.
/// </summary>
[CardName("Temple of Epiphany")]
public static class TempleOfEpiphanyFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-epiphany");

    /// <summary>Construct Temple of Epiphany owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
