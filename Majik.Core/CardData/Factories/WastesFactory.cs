using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wastes.
///
/// Type line: Basic Land  (no land subtype)
/// Oracle text: {T}: Add {C}.
///
/// Wastes is the only basic land with no land subtype (CR 205.3i lists the
/// basic land types; Wastes is not among them). Because it lacks a basic land
/// subtype it gains no intrinsic mana ability from CR 305.6; instead the
/// "{T}: Add {C}" ability is printed directly on the card. Here it is expressed
/// explicitly as a <see cref="Majik.Core.Abilities.ManaAbility"/> producing one
/// colorless mana ({C}) in the card definition, so the card carries its mana
/// ability when built through the data-driven <see cref="CardDefinitionFactory"/>.
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/wastes.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
/// </summary>
[CardName("Wastes")]
public static class WastesFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("wastes");

    /// <summary>Construct a Wastes owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
