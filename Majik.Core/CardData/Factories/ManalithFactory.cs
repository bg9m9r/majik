using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Manalith (multiple printings, {3}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color."
///
/// ## Implemented (v1)
/// - Artifact body / identity / owner / controller built from
///   <c>Majik.Core/CardData/Cards/manalith.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add one mana of any color</b> — five
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per WUBRG),
///   carried in the JSON definition as five <c>{ "kind": "mana" }</c> entries.
///   This is the same any-colour modelling used by Ancient Ziggurat
///   (CR 605.1a — "any color" resolves to five distinct single-colour mana
///   abilities). Unlike Delighted Halfling there is no
///   <see cref="Majik.Core.Mana.SpendRestriction"/>: Manalith's mana is
///   unrestricted. The implicit {T} self-tap is baked into each mana
///   ability's cost.
/// </summary>
[CardName("Manalith")]
public static class ManalithFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("manalith");

    /// <summary>
    /// Construct Manalith owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
