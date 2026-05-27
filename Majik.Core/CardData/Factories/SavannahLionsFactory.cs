using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Savannah Lions (Alpha / Revised / Modern reprints,
/// {W}).
///
/// Creature — Cat 2/1. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A Classic white weenie: 2 power and 1 toughness for a
/// single White mana. One of the original efficiency benchmarks for aggressive
/// white strategies.
///
/// ## Implementation
///
/// - 2/1 <see cref="Creature"/> with <see cref="CardSubtype.Cat"/>.
/// - Mana cost {W}; <see cref="ManaCost"/>'s parser derives White from the
///   single coloured pip (CR 105.2). Mana value = 1.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Savannah Lions")]
public static class SavannahLionsFactory
{
    public const string CardName = "Savannah Lions";
    public const string PrintedManaCost = "{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Savannah Lions — a vanilla {W} 2/1 Creature — Cat.
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
            subtypes: new[] { CardSubtype.Cat });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
