using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fear of Lost Teeth (Duskmourn: House of Horror, {B}).
///
/// Enchantment Creature — Nightmare 1/1. Oracle text (verified against
/// Scryfall):
///   "When this creature dies, it deals 1 damage to any target and you gain
///    1 life."
///
/// The card's base shape (name, Creature + Enchantment types, Nightmare
/// subtype, {B}, 1/1) AND the dies drain trigger are materialised entirely
/// from the embedded JSON definition (<c>fear-of-lost-teeth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same fully-declarative posture
/// as <see cref="HaywireMiteFactory"/> / <see cref="VindictiveVampireFactory"/>.
///
/// ## Implemented (v1)
/// - 1/1 black Nightmare Enchantment Creature at printed cost {B} (mana value
///   1), dual Creature + Enchantment type (CR 301.1 / 302.1 — enchantment
///   creature).
/// - <b>Dies trigger (CR 603.6c / CR 700.4)</b>: a <c>dies_self</c> trigger
///   builds a <see cref="TriggeredAbility"/> over the Battlefield → Graveyard
///   move (active zones {Battlefield, Graveyard} so it survives the death
///   zone change — supplied by the trigger def). On resolution it
///     * deals 1 damage to a chosen <c>any</c> target (player / creature /
///       planeswalker — CR 115.3 / 306.7), declared via the
///       <c>deal_damage</c> verb's any-target slot and read off
///       <see cref="TriggeredAbility.ChosenTargets"/> (CR 608.2b — a null /
///       illegal pick fizzles that half); and
///     * gains the controller 1 life (CR 119.3) via <c>gain_life_self</c>.
///   The damage and the lifegain are separate life-change events (no
///   lifelink).
///
/// ## Deferred (v1 gaps)
/// - <b>Source-attribution of the ping</b>: the printed text reads "it deals 1
///   damage", but the declarative <c>deal_damage</c> verb sources the damage
///   from the effect's controller rather than the (now-dead) permanent itself.
///   This only matters for damage-redirection / damage-doubling effects keyed
///   on the exact source object; the dealt amount and life totals are
///   identical.
/// </summary>
[CardName("Fear of Lost Teeth")]
public static class FearOfLostTeethFactory
{
    public const string CardName = "Fear of Lost Teeth";
    public const string Slug = "fear-of-lost-teeth";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Fear of Lost Teeth owned and controlled by
    /// <paramref name="owner"/> with the dies trigger attached but NOT
    /// registered with a <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Fear of Lost Teeth with an optional <see cref="TriggerManager"/>.
    /// When supplied, the dies trigger is registered so a Battlefield →
    /// Graveyard move places it on the stack automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        if (triggers != null)
        {
            foreach (var trigger in card.Abilities.OfType<TriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(trigger);
            }
        }

        return card;
    }
}
