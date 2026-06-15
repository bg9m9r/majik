using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gilded Lotus (multiple printings, {5}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add three mana of any one color."
///
/// ## Implemented (v1)
/// - Artifact body / identity / owner / controller built from
///   <c>Majik.Core/CardData/Cards/gilded-lotus.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add three mana of any one color</b> — five
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per WUBRG),
///   carried in the JSON definition as five <c>{ "kind": "mana" }</c> entries
///   whose <c>produces</c> is the tripled colour pip (<c>"WWW"</c>, <c>"UUU"</c>,
///   …). Each ability therefore taps the lotus and credits three mana of the
///   chosen colour. This is the same any-one-colour modelling used by
///   Manalith (<see cref="ManalithFactory"/>) scaled from one to three pips,
///   and the same shape as Lotus Bloom's "three mana of any one color"
///   activation minus the Suspend wrapper and the sacrifice rider — Gilded
///   Lotus is a permanent fixture that stays on the battlefield (CR 605.1a —
///   "any one color" resolves to five distinct single-colour mana abilities;
///   CR 605.1b — only one mode fires per tap because the shared {T} cost is
///   already paid). The implicit {T} self-tap is baked into each mana
///   ability's cost.
/// </summary>
[CardName("Gilded Lotus")]
public static class GildedLotusFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("gilded-lotus");

    /// <summary>
    /// Construct Gilded Lotus owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
