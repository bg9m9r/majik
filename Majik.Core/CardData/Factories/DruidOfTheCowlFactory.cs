using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Druid of the Cowl (Aether Revolt, {1}{G}).
///
/// Creature — Elf Druid 1/3. Oracle text:
///   "{T}: Add {G}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/druid-of-the-cowl.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The single
/// "{T}: Add {G}" mana ability (CR 605.1) is declared in the JSON as a
/// <c>{ "kind": "mana", "produces": "G" }</c> entry — same shape used by
/// <see cref="DelightedHalflingFactory"/> (one ManaAbility per declared colour).
///
/// Summoning sickness (CR 302.6 / 605.3a) gating is the engine's
/// responsibility, not this factory's.
/// </summary>
[CardName("Druid of the Cowl")]
public static class DruidOfTheCowlFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("druid-of-the-cowl");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
