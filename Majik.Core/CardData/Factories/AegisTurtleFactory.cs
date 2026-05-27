using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aegis Turtle (Core Set 2021, {U}).
///
/// Creature — Turtle 0/5. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A defensive blue one-drop: 0 power and 5 toughness
/// for a single Blue mana; one of the most durable blockers at one mana.
///
/// ## Implementation
///
/// - 0/5 <see cref="Creature"/> with <see cref="CardSubtype.Turtle"/>.
/// - Mana cost {U}; <see cref="ManaCost"/>'s parser derives Blue from the
///   single coloured pip (CR 105.2). Mana value = 1.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Aegis Turtle")]
public static class AegisTurtleFactory
{
    public const string CardName = "Aegis Turtle";
    public const string PrintedManaCost = "{U}";
    public const int Power = 0;
    public const int Toughness = 5;

    /// <summary>
    /// Constructs Aegis Turtle — a vanilla {U} 0/5 Creature — Turtle.
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
            subtypes: new[] { CardSubtype.Turtle });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
