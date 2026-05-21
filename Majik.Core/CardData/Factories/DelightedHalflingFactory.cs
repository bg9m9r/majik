using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Delighted Halfling (The Lord of the Rings: Tales of
/// Middle-earth).
///
/// Legendary Creature — Halfling Citizen 1/2.
/// Oracle text:
///   "{T}: Add one mana of any color. Spend this mana only to cast a legendary
///    spell. That spell can't be countered."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/delighted-halfling.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The
/// "any color" mana ability is modeled as five <see cref="Abilities.ManaAbility"/>
/// instances (one per WUBRG) — mirrors the Treasure-token pattern; the
/// mana picker can satisfy any single colour pip via this creature.
///
/// ## Deferred (v1 gaps)
/// - <b>Usage restriction</b>: "Spend this mana only to cast a legendary
///   spell." Enforcement requires per-mana-pool entry tagging and a
///   spend-restriction check in the cast-payment flow. Not yet retrofitted.
/// - <b>Can't-be-countered rider</b>: "That spell can't be countered."
///   Requires flagging the spell object at cast time and gating
///   counter-spells in <see cref="Majik.Core.Services.StackResolver"/>.
///   Deferred.
/// </summary>
public static class DelightedHalflingFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("delighted-halfling");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
