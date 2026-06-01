using Majik.Core.Cards.Types;

namespace Majik.Core.Cards;

/// <summary>
/// Construction helpers for multi-type permanents whose concrete subclass
/// only registers a single card type. The base <see cref="Creature"/> ctor
/// stamps <see cref="CardType.Creature"/> only; an Enchantment Creature
/// (CR 205.2a — Sanctum Weaver, the Theros gods, Spirited Companion, etc.)
/// carries BOTH Creature AND Enchantment. Factories used to do this ad-hoc
/// via <see cref="Card.AddCardType"/>; this helper centralises the additive
/// stamp so every Enchantment-Creature factory produces a consistent type
/// set and "count enchantments / count creatures" predicates see them.
/// </summary>
public static class PermanentBuilders
{
    /// <summary>
    /// CR 205.2a — build an Enchantment Creature: a <see cref="Creature"/>
    /// shell with <see cref="CardType.Enchantment"/> additively stamped on
    /// top of the printed <see cref="CardType.Creature"/>. The result has
    /// both card types, so it counts toward both "creatures you control"
    /// and "enchantments you control" once those predicates read the
    /// computed/printed type set.
    /// </summary>
    public static Creature EnchantmentCreature(
        string name,
        string manaCost,
        int power,
        int toughness,
        IEnumerable<CardSupertype>? supertypes = null,
        IEnumerable<CardSubtype>? subtypes = null)
    {
        var card = new Creature(name, manaCost, power, toughness, supertypes, subtypes);
        // CR 301.1 / 302.1 — additively stamp Enchantment on top of Creature.
        card.AddCardType(CardType.Enchantment);
        return card;
    }
}
