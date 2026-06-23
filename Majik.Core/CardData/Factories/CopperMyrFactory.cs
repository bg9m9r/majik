using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Copper Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {G}."
///
/// The green member of the five mono-colour Myr mana dorks (Iron = {R},
/// Copper = {G}, Gold = {W}, Leaden = {B}, Silver = {U}). An Artifact
/// Creature body that ramps a single green pip and plays well with the
/// affinity / artifact-matters shells.
///
/// Loads <c>Majik.Core/CardData/Cards/copper-myr.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The entire
/// printed shape is expressed by the JSON definition:
/// <list type="bullet">
///   <item>The <c>types</c> array carries both Creature and Artifact, so
///   <see cref="Card.HasType"/> surfaces the artifact type for affinity /
///   artifact-matters consumers (CR 301.1 / 302.1).</item>
///   <item>The single <c>{ "kind": "mana", "produces": "G" }</c> ability
///   becomes one <see cref="Abilities.ManaAbility"/> producing {G}
///   (CR 605.1). With no additional cost it uses the {T}-only mana-ability
///   shape, whose default gate is <c>!IsTapped</c> — a tapped myr can't
///   re-tap. Summoning sickness (CR 302.6) is enforced by the engine at
///   activation time.</item>
/// </list>
///
/// No factory-layered behaviour is needed — Copper Myr's oracle text is fully
/// covered by the JSON schema (same JSON-backed posture as
/// <see cref="IronMyrFactory"/>, with {R} -> {G}).
/// </summary>
[CardName("Copper Myr")]
public static class CopperMyrFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("copper-myr");

    public static Creature Create(Player owner)
        => (Creature)CardDefinitionFactory.Build(Definition, owner);
}
