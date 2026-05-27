using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Piker (Magic 2010 / Modern reprints,
/// {1}{R}).
///
/// Creature — Goblin Warrior 2/1. Vanilla — no printed keywords, triggers,
/// statics, or activated abilities. A classic red aggro body: 2 power and 1
/// toughness for one generic and one Red mana (mana value 2).
///
/// ## Implementation
///
/// - 2/1 <see cref="Creature"/> with <see cref="CardSubtype.Goblin"/> and
///   <see cref="CardSubtype.Warrior"/>.
/// - Mana cost {1}{R}; <see cref="ManaCost"/>'s parser derives Red from the
///   single coloured pip (CR 105.2). Mana value = 2.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Goblin Piker")]
public static class GoblinPikerFactory
{
    public const string CardName = "Goblin Piker";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Goblin Piker — a vanilla {1}{R} 2/1 Creature — Goblin Warrior.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
