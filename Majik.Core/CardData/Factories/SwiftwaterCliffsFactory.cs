using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Swiftwater Cliffs (Khans of Tarkir "gain-life
/// tapland" cycle — the Innistrad "Refuge" land shape).
///
/// U/R gain-life land. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {U} or {R}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/swiftwater-cliffs.json</c>. Same oracle shape
/// as <see cref="RuggedHighlandsFactory"/> (dual-colour mana ability + a
/// self-ETB gain-1-life trigger, CR 119); only the produced colours differ —
/// {U}/{R} instead of {R}/{G}. Unconditional ETB-tapped (CR 614.1c) is applied
/// on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience — matches the refuge-land cycle).
/// </summary>
[CardName("Swiftwater Cliffs")]
public static class SwiftwaterCliffsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("swiftwater-cliffs");

    /// <summary>Construct Swiftwater Cliffs owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
