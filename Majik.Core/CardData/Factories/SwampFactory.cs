using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Swamp.
///
/// Type line: Basic Land — Swamp
/// Oracle text: ({T}: Add {B}.)
///
/// The {T}: Add {B} mana ability is intrinsic to the Swamp land subtype
/// (CR 305.6 — a land with the Swamp subtype has the intrinsic ability
/// "{T}: Add {B}"). Here it is expressed explicitly as a
/// <see cref="Majik.Core.Abilities.ManaAbility"/> in the card definition so
/// the card carries its mana ability when built through the data-driven
/// <see cref="CardDefinitionFactory"/> route (independent of the inline
/// <c>AttachBasicLandMana</c> shortcut used by <see cref="NamedCardFactory"/>).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/swamp.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
/// </summary>
[CardName("Swamp")]
public static class SwampFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("swamp");

    /// <summary>Construct a Swamp owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
