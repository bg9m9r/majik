using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wind-Scarred Crag (Khans of Tarkir).
///
/// R/W "life-gain dual land" (the Refuge cycle). Oracle text (verified
/// against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {R} or {W}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/wind-scarred-crag.json</c>. Same oracle shape
/// as the rest of the Refuge cycle (<see cref="TranquilCoveFactory"/>): a flat
/// "you gain 1 life" ETB keyword action (CR 119.3) plus two single-colour mana
/// abilities producing {R} and {W} (CR 605.1a). Unconditional ETB-tapped
/// (CR 614.1c) is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience — matches the rest of the cycle).
/// </summary>
[CardName("Wind-Scarred Crag")]
public static class WindScarredCragFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("wind-scarred-crag");

    /// <summary>Construct Wind-Scarred Crag owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
