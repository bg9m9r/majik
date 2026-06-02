using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gold Myr (Mirrodin, {2}).
///
/// Artifact Creature — Myr 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {W}."
///
/// The white member of the original Mirrodin mana-Myr cycle (Gold / Silver /
/// Iron / Copper / Leaden), each an Artifact Creature — Myr 1/1 that taps for
/// one pip of its colour. Gold Myr produces {W}.
///
/// Loads <c>Majik.Core/CardData/Cards/gold-myr.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The JSON
/// <c>types</c> array carries both Creature and Artifact, so
/// <see cref="Card.HasType"/> surfaces the artifact type for affinity /
/// artifact-matters consumers (CR 301.1 / 302.1).
///
/// The single JSON mana ability (<c>{ "kind": "mana", "produces": "W" }</c>)
/// materializes as a <see cref="Abilities.ManaAbility"/> whose activation taps
/// the myr as its cost ({T}) and adds one {W} (CR 605.1). The default
/// <c>!IsTapped</c> activation gate prevents a second activation while the myr
/// is already tapped; summoning sickness (CR 302.6) is enforced by the engine
/// at activation time, not baked here.
/// </summary>
[CardName("Gold Myr")]
public static class GoldMyrFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("gold-myr");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
