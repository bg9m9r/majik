using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rugged Highlands (Innistrad "Refuge" gain-life
/// tapland cycle).
///
/// R/G gain-life land. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {R} or {G}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/rugged-highlands.json</c>. Same oracle shape
/// as <see cref="TempleOfAbandonFactory"/> (R/G tapland with a dual-colour
/// mana ability and a self-ETB trigger); only the ETB keyword action differs
/// — gain 1 life (CR 119) instead of scry 1. Unconditional ETB-tapped
/// (CR 614.1c) is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience — matches the temple / scry-land
/// cycle).
/// </summary>
[CardName("Rugged Highlands")]
public static class RuggedHighlandsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("rugged-highlands");

    /// <summary>Construct Rugged Highlands owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
