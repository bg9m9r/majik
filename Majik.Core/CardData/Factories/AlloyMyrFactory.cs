using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alloy Myr (Scars of Mirrodin, {3}).
///
/// Artifact Creature — Myr 2/2. Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color."
///
/// A bigger cousin of the Mirrodin mana-Myr cycle: it shares the
/// <see cref="GoldMyrFactory">Gold Myr</see> body shape (dual Artifact +
/// Creature, Myr subtype) but is a {3} 2/2 and taps for any colour instead of
/// a single fixed pip.
///
/// Loads <c>Majik.Core/CardData/Cards/alloy-myr.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The JSON
/// <c>types</c> array carries both Creature and Artifact, so
/// <see cref="Card.HasType"/> surfaces the artifact type for affinity /
/// artifact-matters consumers (CR 301.1 / 302.1).
///
/// "Add one mana of any color" is modelled the same way as
/// <see cref="ManalithFactory">Manalith</see>: five distinct
/// <see cref="Abilities.ManaAbility"/> slots (one per WUBRG), each carried in
/// the JSON definition as a <c>{ "kind": "mana" }</c> entry (CR 605.1a). The
/// implicit {T} self-tap cost is baked into each mana ability; the default
/// <c>!IsTapped</c> activation gate prevents a second activation while the myr
/// is already tapped, and summoning sickness (CR 302.6) is enforced by the
/// engine at activation time, not baked here.
/// </summary>
[CardName("Alloy Myr")]
public static class AlloyMyrFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("alloy-myr");

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
