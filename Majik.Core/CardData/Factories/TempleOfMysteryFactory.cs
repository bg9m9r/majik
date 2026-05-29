using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Mystery (Theros Beyond Death).
///
/// G/U "scry land". Oracle text:
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {G} or {U}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/temple-of-mystery.json</c>. Same oracle
/// shape as the rest of the Temple scry-land cycle
/// (<see cref="TempleOfTriumphFactory"/>), only the produced colours are
/// {G}/{U} (CR 605.1a). The ETB keyword action is scry 1 (CR 701.20).
/// Unconditional ETB-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory
/// builds the land without it, for test convenience — matches the rest of the
/// cycle). Scry decision is agent-driven via
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseScryDecisionAsync"/>
/// when registered, otherwise default all-to-bottom.
/// </summary>
[CardName("Temple of Mystery")]
public static class TempleOfMysteryFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-mystery");

    /// <summary>Construct Temple of Mystery owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
