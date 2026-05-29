using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Island.
///
/// Type line: Basic Land — Island
/// Oracle text: ({T}: Add {U}.)
///
/// The {T}: Add {U} mana ability is intrinsic to the Island land subtype
/// (CR 305.6 — a land with the Island subtype has the intrinsic ability
/// "{T}: Add {U}"). Here it is expressed explicitly as a
/// <see cref="Majik.Core.Abilities.ManaAbility"/> in the card definition so
/// the card carries its mana ability when built through the data-driven
/// <see cref="CardDefinitionFactory"/> route (independent of the inline
/// <c>AttachBasicLandMana</c> shortcut used by <see cref="NamedCardFactory"/>).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/island.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
/// </summary>
[CardName("Island")]
public static class IslandFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("island");

    /// <summary>Construct an Island owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
