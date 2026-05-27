using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vastwood Gorger (Magic 2012 / Modern reprints,
/// {5}{G}).
///
/// Creature — Wurm 5/6. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A large green beatstick: 5 power and 6 toughness for
/// five generic and one Green mana (mana value 6).
///
/// ## Implementation
///
/// - 5/6 <see cref="Creature"/> with <see cref="CardSubtype.Wurm"/>.
/// - Mana cost {5}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   coloured pip (CR 105.2). Mana value = 6.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Vastwood Gorger")]
public static class VastwoodGorgerFactory
{
    public const string CardName = "Vastwood Gorger";
    public const string PrintedManaCost = "{5}{G}";
    public const int Power = 5;
    public const int Toughness = 6;

    /// <summary>
    /// Constructs Vastwood Gorger — a vanilla {5}{G} 5/6 Creature — Wurm.
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
            subtypes: new[] { CardSubtype.Wurm });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
