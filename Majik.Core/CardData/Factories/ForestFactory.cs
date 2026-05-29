using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Forest.
///
/// Type line: Basic Land — Forest
/// Oracle text: ({T}: Add {G}.)
///
/// The {T}: Add {G} mana ability is intrinsic to the Forest land subtype
/// (CR 305.6 — a land with the Forest subtype has the intrinsic ability
/// "{T}: Add {G}"). Here it is expressed explicitly as a
/// <see cref="Majik.Core.Abilities.ManaAbility"/> in the card definition so
/// the card carries its mana ability when built through the data-driven
/// <see cref="CardDefinitionFactory"/> route (independent of the inline
/// <c>AttachBasicLandMana</c> shortcut used by <see cref="NamedCardFactory"/>).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/forest.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
/// </summary>
[CardName("Forest")]
public static class ForestFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("forest");

    /// <summary>Construct a Forest owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
