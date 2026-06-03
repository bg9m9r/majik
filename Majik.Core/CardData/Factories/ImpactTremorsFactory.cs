using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Impact Tremors (Dragons of Tarkir, {1}{R}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Whenever a creature you control enters, this enchantment deals 1 damage
///   to each opponent."
///
/// The card's base shape (name, Enchantment, {1}{R}) and the "creature you
/// control enters → 1 damage to each opponent" trigger are materialised
/// entirely from the embedded JSON definition (<c>impact-tremors.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — no code-side wiring is needed.
///
/// ## Implemented (v1)
/// - Enchantment at printed cost {1}{R} (mana value 2).
/// - <b>Triggered ability (CR 603.6e / CR 109.5)</b>: a
///   <c>whenever_another_creature_enters</c> trigger scoped to the controller's
///   creatures (<c>youControlOnly</c> + <c>includeSelf</c> — "a creature you
///   control", which counts any creature entering under the controller). On
///   resolution the new <c>deal_damage_each_opponent</c> verb deals 1 damage to
///   every player OTHER than the controller (CR 109.5 "each opponent"), routed
///   through the shared <c>Fx.DealDamageAny</c> primitive. Untargeted — a group
///   effect (CR 608.2), so no target is announced and nothing can be removed in
///   response. This is the declarative pay-down of the
///   opponent-scoped-deal-damage rider shape.
/// </summary>
[CardName("Impact Tremors")]
public static class ImpactTremorsFactory
{
    public const string CardName = "Impact Tremors";
    public const string Slug = "impact-tremors";

    /// <summary>
    /// Construct Impact Tremors owned and controlled by <paramref name="owner"/>.
    /// Base shape + the deal-1-to-each-opponent trigger come from the embedded
    /// JSON definition.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Enchantment)CardDefinitionFactory.Build(definition, owner);
    }
}
