using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Graypelt Refuge (Zendikar, et al.).
///
/// G/W "life-gain dual land" (the Refuge cycle). Oracle text (verified
/// against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {G} or {W}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/graypelt-refuge.json</c>. Same oracle shape as
/// <see cref="ScouredBarrensFactory"/>, only the produced colours differ
/// ({G}/{W} instead of {W}/{B}). The ETB keyword action is a flat "you gain 1
/// life" (CR 119.3); the two single-colour mana abilities produce {G} and {W}
/// (CR 605.1a). Unconditional ETB-tapped (CR 614.1c) is applied on the
/// production load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>
/// (this factory builds the land without it, for test convenience — matches
/// the Refuge / Temple cycle posture).
/// </summary>
[CardName("Graypelt Refuge")]
public static class GraypeltRefugeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("graypelt-refuge");

    /// <summary>Construct Graypelt Refuge owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
