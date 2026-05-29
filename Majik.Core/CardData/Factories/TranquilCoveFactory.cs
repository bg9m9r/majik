using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tranquil Cove (Khans of Tarkir).
///
/// W/U "life-gain dual land" (the Refuge cycle). Oracle text (verified
/// against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {W} or {U}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/tranquil-cove.json</c>. Same oracle shape as
/// the scry-land Temple cycle (<see cref="TempleOfEnlightenmentFactory"/>),
/// only the ETB keyword action is a flat "you gain 1 life" (CR 119.3) in
/// place of scry 1. The two single-colour mana abilities produce {W} and
/// {U} (CR 605.1a). Unconditional ETB-tapped (CR 614.1c) is applied on the
/// production load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>
/// (this factory builds the land without it, for test convenience — matches
/// the Temple / gain-land cycle posture).
/// </summary>
[CardName("Tranquil Cove")]
public static class TranquilCoveFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("tranquil-cove");

    /// <summary>Construct Tranquil Cove owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
