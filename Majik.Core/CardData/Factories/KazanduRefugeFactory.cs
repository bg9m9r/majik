using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kazandu Refuge (Zendikar "Refuge" gain-life
/// tapland cycle).
///
/// R/G gain-life land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {R} or {G}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/kazandu-refuge.json</c>. Identical oracle
/// shape to <see cref="RuggedHighlandsFactory"/> (Innistrad R/G Refuge): a
/// dual-colour mana ability and a self-ETB "gain 1 life" trigger (CR 119).
/// Unconditional ETB-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory
/// builds the land without it, for test convenience — matches the rest of the
/// Refuge cycle).
/// </summary>
[CardName("Kazandu Refuge")]
public static class KazanduRefugeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("kazandu-refuge");

    /// <summary>Construct Kazandu Refuge owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
