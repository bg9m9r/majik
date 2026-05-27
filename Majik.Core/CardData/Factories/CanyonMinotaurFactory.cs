using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Canyon Minotaur (Magic 2010, {3}{R}).
///
/// Creature — Minotaur Warrior 3/3. Vanilla — no printed keywords,
/// triggers, statics, or activated abilities.
///
/// ## Implementation
///
/// - 3/3 <see cref="Creature"/> with subtypes
///   <see cref="CardSubtype.Minotaur"/> and
///   <see cref="CardSubtype.Warrior"/> (CR 205.3m).
/// - Mana cost {3}{R} — 3 generic + 1 red; CMC 4 (CR 202.3).
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Canyon Minotaur")]
public static class CanyonMinotaurFactory
{
    public const string CardName = "Canyon Minotaur";
    public const string PrintedManaCost = "{3}{R}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Canyon Minotaur — a vanilla {3}{R} 3/3 Creature —
    /// Minotaur Warrior.
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
            subtypes: new[] { CardSubtype.Minotaur, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
