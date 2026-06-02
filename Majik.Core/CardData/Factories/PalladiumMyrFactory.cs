using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Palladium Myr (Scars of Mirrodin, {4}).
///
/// Artifact Creature — Myr 2/2. Oracle text (verified against Scryfall):
///   "{T}: Add {C}{C}."
///
/// The bigger cousin of the Mirrodin mana-Myr cycle (Gold / Silver / Iron /
/// Copper / Leaden): a 2/2 at {4} that taps for two colourless rather than
/// one coloured pip, so it nets one mana of ramp per activation.
///
/// Loads <c>Majik.Core/CardData/Cards/palladium-myr.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The JSON
/// <c>types</c> array carries both Creature and Artifact, so
/// <see cref="Card.HasType"/> surfaces the artifact type for affinity /
/// artifact-matters consumers (CR 301.1 / 302.1).
///
/// The single JSON mana ability (<c>{ "kind": "mana", "produces": "CC" }</c>)
/// materializes as a <see cref="Abilities.ManaAbility"/> whose activation taps
/// the myr as its cost ({T}) and adds {C}{C} (CR 605.1). Each {C} is bucketed
/// as +1 generic in <see cref="ValueObjects.ManaCost.Parse"/> today (CR 107.4c —
/// no dedicated colourless bucket; same convention as Plague Myr / Mind Stone /
/// Worn Powerstone), so the two pips surface as +2 generic. The default
/// <c>!IsTapped</c> activation gate prevents a second activation while the myr
/// is already tapped; summoning sickness (CR 302.6) is enforced by the engine
/// at activation time, not baked here.
/// </summary>
[CardName("Palladium Myr")]
public static class PalladiumMyrFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("palladium-myr");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
