using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scoured Barrens (Khans of Tarkir, et al.).
///
/// W/B "life-gain dual land" (the Refuge cycle). Oracle text (verified
/// against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {W} or {B}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/scoured-barrens.json</c>. Same oracle shape as
/// <see cref="TranquilCoveFactory"/>, only the produced colours differ ({W}/{B}
/// instead of {W}/{U}). The ETB keyword action is a flat "you gain 1 life"
/// (CR 119.3); the two single-colour mana abilities produce {W} and {B}
/// (CR 605.1a). Unconditional ETB-tapped (CR 614.1c) is applied on the
/// production load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>
/// (this factory builds the land without it, for test convenience — matches
/// the Refuge / Temple cycle posture).
/// </summary>
[CardName("Scoured Barrens")]
public static class ScouredBarrensFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("scoured-barrens");

    /// <summary>Construct Scoured Barrens owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
