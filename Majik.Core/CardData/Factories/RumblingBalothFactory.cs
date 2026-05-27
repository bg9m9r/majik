using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rumbling Baloth (Magic 2011 / Modern reprints,
/// {2}{G}{G}).
///
/// Creature — Beast 4/4. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A solid green beater: 4 power and 4 toughness for two
/// generic and two Green mana (mana value 4).
///
/// ## Implementation
///
/// - 4/4 <see cref="Creature"/> with <see cref="CardSubtype.Beast"/>.
/// - Mana cost {2}{G}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   two coloured pips (CR 105.2). Mana value = 4.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Rumbling Baloth")]
public static class RumblingBalothFactory
{
    public const string CardName = "Rumbling Baloth";
    public const string PrintedManaCost = "{2}{G}{G}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Constructs Rumbling Baloth — a vanilla {2}{G}{G} 4/4 Creature — Beast.
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
