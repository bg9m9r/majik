using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Birds of Paradise (a staple green mana dork,
/// {G}).
///
/// Creature — Bird 0/1.
/// Oracle text:
///   "Flying
///    {T}: Add one mana of any color."
///
/// Loads <c>Majik.Core/CardData/Cards/birds-of-paradise.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The "add one
/// mana of any color" ability is modeled as five
/// <see cref="Abilities.ManaAbility"/> instances (one per WUBRG) in the JSON
/// — mirrors the Delighted Halfling / Treasure-token pattern; the mana picker
/// can satisfy any single colour pip via this creature.
///
/// ## Flying (CR 702.9)
/// The JSON card-definition schema does not yet carry evergreen keyword
/// markers, so Flying is wired here as a <see cref="KeywordAbility"/> marker
/// after the definition is built — the same shape Wall of Swords uses. This
/// surfaces <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
/// <c>CanBlockFlying</c> for evasion + block-legality consumers.
/// </summary>
[CardName("Birds of Paradise")]
public static class BirdsOfParadiseFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("birds-of-paradise");

    public static Creature Create(Player owner)
    {
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker so
        // CombatAbilities.HasFlying / CanBlockFlying surface evasion
        // enforcement and block-legality checks. Added here because the
        // JSON definition schema has no keyword field yet.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
