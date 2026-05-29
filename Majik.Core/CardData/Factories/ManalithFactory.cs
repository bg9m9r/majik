using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Manalith (Tenth Edition and friends).
///
/// Artifact {3}. Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/manalith.json</c> and builds through
/// <see cref="CardDefinitionFactory"/>. Same "tap-for-mana rock" body as
/// <see cref="MindStoneFactory"/>, but the single {C} slot is replaced by
/// five single-colour <see cref="Majik.Core.Abilities.ManaAbility"/> slots —
/// one per WUBRG colour (CR 605.1a). "Add one mana of any color" is modelled
/// as five distinct mana-ability slots (sibling shape to Springleaf Drum /
/// Crumbling Vestige) so the activator picks the colour by picking the
/// matching slot; no separate colour prompt is required. The implicit {T}
/// self-tap is baked into <c>ManaAbility</c>'s simple constructor
/// (<c>_tapsAsCost = true</c>), so the JSON carries no explicit cost. CR 605.1
/// — mana abilities don't use the stack.
/// </summary>
[CardName("Manalith")]
public static class ManalithFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("manalith");

    /// <summary>Construct Manalith owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
