using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plains.
///
/// Type line: Basic Land — Plains
/// Oracle text: ({T}: Add {W}.)
///
/// The {T}: Add {W} mana ability is intrinsic to the Plains land subtype
/// (CR 305.6 — a land with the Plains subtype has the intrinsic ability
/// "{T}: Add {W}"). Here it is expressed explicitly as a
/// <see cref="Majik.Core.Abilities.ManaAbility"/> in the card definition so
/// the card carries its mana ability when built through the data-driven
/// <see cref="CardDefinitionFactory"/> route (independent of the inline
/// <c>AttachBasicLandMana</c> shortcut used by <see cref="NamedCardFactory"/>).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/plains.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
/// </summary>
[CardName("Plains")]
public static class PlainsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("plains");

    /// <summary>Construct a Plains owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
