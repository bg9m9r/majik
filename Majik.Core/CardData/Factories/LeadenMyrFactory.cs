using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leaden Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {B}."
///
/// The black member of the original Mirrodin mana-Myr cycle (Gold / Silver /
/// Iron / Copper / Leaden), each an Artifact Creature — Myr 1/1 that taps for
/// one pip of its colour. Leaden Myr produces {B}.
///
/// Loads <c>Majik.Core/CardData/Cards/leaden-myr.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The JSON
/// <c>types</c> array carries both Creature and Artifact, so
/// <see cref="Card.HasType"/> surfaces the artifact type for affinity /
/// artifact-matters consumers (CR 301.1 / 302.1).
///
/// The single JSON mana ability (<c>{ "kind": "mana", "produces": "B" }</c>)
/// materializes as a <see cref="Abilities.ManaAbility"/> whose activation taps
/// the myr as its cost ({T}) and adds one {B} (CR 605.1). The default
/// <c>!IsTapped</c> activation gate prevents a second activation while the myr
/// is already tapped; summoning sickness (CR 302.6) is enforced by the engine
/// at activation time, not baked here.
/// </summary>
[CardName("Leaden Myr")]
public static class LeadenMyrFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("leaden-myr");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
