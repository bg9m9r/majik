using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Enlightenment (Born of the Gods).
///
/// W/U "scry land". Oracle text:
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {W} or {U}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/temple-of-enlightenment.json</c>. Same oracle
/// shape as <see cref="TempleOfTriumphFactory"/>, only the two single-colour
/// mana abilities produce {W} and {U} (CR 605.1a). The ETB keyword action is
/// scry 1 (CR 701.20). Unconditional ETB-tapped (CR 614.1c) is applied on the
/// production load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>
/// (this factory builds the land without it, for test convenience — matches
/// the Temple cycle posture). Scry decision is agent-driven via
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseScryDecisionAsync"/>
/// when registered, otherwise default all-to-bottom.
/// </summary>
[CardName("Temple of Enlightenment")]
public static class TempleOfEnlightenmentFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-enlightenment");

    /// <summary>Construct Temple of Enlightenment owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
