using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thornwood Falls (Khans of Tarkir "Refuge" gain-life
/// tapland cycle).
///
/// G/U gain-life land. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {G} or {U}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/thornwood-falls.json</c>. Same oracle shape as
/// <see cref="RuggedHighlandsFactory"/> (refuge tapland with a dual-colour
/// mana ability and a self-ETB gain-life trigger); only the colour pair
/// differs — {G}/{U} instead of {R}/{G}. Unconditional ETB-tapped
/// (CR 614.1c) is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience — matches the rest of the refuge
/// cycle).
/// </summary>
[CardName("Thornwood Falls")]
public static class ThornwoodFallsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("thornwood-falls");

    /// <summary>Construct Thornwood Falls owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
