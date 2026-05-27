using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ancient Crab (Amonkhet, {1}{U}{U}).
///
/// Creature — Crab 1/5. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A defensive blue three-drop: 1 power and 5 toughness
/// for one generic and two Blue mana; a durable blocker with an unusually high
/// toughness-to-mana ratio for its cost.
///
/// ## Implementation
///
/// - 1/5 <see cref="Creature"/> with <see cref="CardSubtype.Crab"/>.
/// - Mana cost {1}{U}{U}; <see cref="ManaCost"/>'s parser derives Blue from
///   the two coloured pips (CR 105.2). Mana value = 3.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Ancient Crab")]
public static class AncientCrabFactory
{
    public const string CardName = "Ancient Crab";
    public const string PrintedManaCost = "{1}{U}{U}";
    public const int Power = 1;
    public const int Toughness = 5;

    /// <summary>
    /// Constructs Ancient Crab — a vanilla {1}{U}{U} 1/5 Creature — Crab.
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
            subtypes: new[] { CardSubtype.Crab });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
