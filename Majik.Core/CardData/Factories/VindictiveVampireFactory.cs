using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vindictive Vampire (Eldritch Moon, {3}{B}).
///
/// Creature — Vampire 2/3. Oracle text (verified against Scryfall):
///   "Whenever another creature you control dies, this creature deals 1 damage
///    to each opponent and you gain 1 life."
///
/// The card's base shape (name, Creature, Vampire subtype, {3}{B}, 2/3) and the
/// aristocrat death trigger are materialised entirely from the embedded JSON
/// definition (<c>vindictive-vampire.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — no code-side wiring is needed.
///
/// ## Implemented (v1)
/// - 2/3 Vampire at printed cost {3}{B} (mana value 4).
/// - <b>Triggered ability (CR 603.6e / CR 109.5 / CR 700.4)</b>: a
///   <c>whenever_another_creature_dies</c> trigger scoped to the controller's
///   OTHER creatures (<c>youControlOnly</c>; <c>includeSelf</c> default false —
///   "another creature you control"). On resolution the
///   <c>deal_damage_each_opponent</c> verb pings every player OTHER than the
///   controller for 1 (CR 109.5 "each opponent", untargeted group effect CR
///   608.2) and the <c>gain_life_self</c> verb gains the controller 1 life
///   (CR 119.3). The damage and the lifegain are separate life-change events
///   (no lifelink). This is the declarative pay-down of the
///   another-creature-dies third-party trigger shape.
///
/// ## Deferred (v1 gaps)
/// - <b>Source-attribution of the ping</b>: the printed text reads "this
///   creature deals 1 damage", but the declarative
///   <c>deal_damage_each_opponent</c> verb sources the damage from the effect's
///   controller rather than the permanent itself. This only matters for
///   damage-redirection / damage-doubling effects keyed on the exact source
///   permanent; the life totals are identical.
/// </summary>
[CardName("Vindictive Vampire")]
public static class VindictiveVampireFactory
{
    public const string CardName = "Vindictive Vampire";
    public const string Slug = "vindictive-vampire";

    /// <summary>
    /// Construct Vindictive Vampire owned and controlled by
    /// <paramref name="owner"/>. Base shape + the another-creature-you-control
    /// dies drain trigger come from the embedded JSON definition.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
