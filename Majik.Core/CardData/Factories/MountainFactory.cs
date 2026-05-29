using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mountain.
///
/// Type line: Basic Land — Mountain
/// Oracle text: ({T}: Add {R}.)
///
/// The {T}: Add {R} mana ability is intrinsic to the Mountain land subtype
/// (CR 305.6 — a land with the Mountain subtype has the intrinsic ability
/// "{T}: Add {R}"). Here it is expressed explicitly as a
/// <see cref="Majik.Core.Abilities.ManaAbility"/> in the card definition so
/// the card carries its mana ability when built through the data-driven
/// <see cref="CardDefinitionFactory"/> route (independent of the inline
/// <c>AttachBasicLandMana</c> shortcut used by <see cref="NamedCardFactory"/>).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/mountain.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
/// </summary>
[CardName("Mountain")]
public static class MountainFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mountain");

    /// <summary>Construct a Mountain owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
