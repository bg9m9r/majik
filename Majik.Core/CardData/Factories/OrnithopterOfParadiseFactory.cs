using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ornithopter of Paradise (March of the Machine,
/// {2}).
///
/// Artifact Creature — Thopter 0/2.
/// Oracle text:
///   "Flying
///    {T}: Add one mana of any color."
///
/// The any-colour fixing twin of Birds of Paradise on the Ornithopter
/// chassis — an Artifact Creature body that pairs with the affinity /
/// artifact-matters shells while fixing all five colours.
///
/// Loads <c>Majik.Core/CardData/Cards/ornithopter-of-paradise.json</c> and
/// lets <see cref="CardDefinitionFactory"/> build the runtime card. The
/// "add one mana of any color" ability is modeled as five
/// <see cref="Abilities.ManaAbility"/> instances (one per WUBRG) in the JSON
/// — the same shape <see cref="BirdsOfParadiseFactory"/> uses; the mana
/// picker can satisfy any single colour pip via this creature.
///
/// The JSON <c>types</c> array carries both Creature and Artifact, so
/// <see cref="Card.HasType"/> surfaces the artifact type for affinity /
/// artifact-matters consumers (CR 301.1 / 302.1).
///
/// ## Flying (CR 702.9)
/// The JSON card-definition schema does not yet carry evergreen keyword
/// markers, so Flying is wired here as a <see cref="KeywordAbility"/> marker
/// after the definition is built — the same shape Birds of Paradise uses.
/// This surfaces <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
/// <c>CanBlockFlying</c> for evasion + block-legality consumers.
/// </summary>
[CardName("Ornithopter of Paradise")]
public static class OrnithopterOfParadiseFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ornithopter-of-paradise");

    public static Creature Create(Player owner)
    {
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker so
        // CombatAbilities.HasFlying / CanBlockFlying surface evasion
        // enforcement and block-legality checks. Added here because the
        // JSON definition schema has no keyword field yet (same as Birds
        // of Paradise).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        return card;
    }
}
