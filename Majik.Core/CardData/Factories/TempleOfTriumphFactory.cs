using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Triumph (Theros Beyond Death).
///
/// R/W "scry land". Oracle text:
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {R} or {W}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/temple-of-triumph.json</c>. Same oracle
/// shape as the Murders at Karlov Manor surveil-land cycle
/// (<see cref="CommercialDistrictFactory"/>), only the ETB keyword action is
/// scry 1 (CR 701.20) instead of surveil 1 (CR 701.42). Unconditional
/// ETB-tapped (CR 614.1c) is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience — matches the surveil-land
/// posture). Scry decision is agent-driven via
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseScryDecisionAsync"/>
/// when registered, otherwise default all-to-bottom.
/// </summary>
[CardName("Temple of Triumph")]
public static class TempleOfTriumphFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-triumph");

    /// <summary>Construct Temple of Triumph owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
