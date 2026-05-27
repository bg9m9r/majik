using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leatherback Baloth (Worldwake / Modern reprints,
/// {G}{G}{G}).
///
/// Creature — Beast 4/5. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A powerful green beatstick: 4 power and 5 toughness for
/// three Green mana (mana value 3). One of the most efficient vanilla creatures
/// in Modern green aggro strategies.
///
/// ## Implementation
///
/// - 4/5 <see cref="Creature"/> with <see cref="CardSubtype.Beast"/>.
/// - Mana cost {G}{G}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   three coloured pips (CR 105.2). Mana value = 3.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Leatherback Baloth")]
public static class LeatherbackBalothFactory
{
    public const string CardName = "Leatherback Baloth";
    public const string PrintedManaCost = "{G}{G}{G}";
    public const int Power = 4;
    public const int Toughness = 5;

    /// <summary>
    /// Constructs Leatherback Baloth — a vanilla {G}{G}{G} 4/5 Creature — Beast.
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
            subtypes: new[] { CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
