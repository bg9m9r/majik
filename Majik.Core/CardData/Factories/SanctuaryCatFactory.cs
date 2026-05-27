using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sanctuary Cat (Amonkhet, {W}).
///
/// Creature — Cat 1/2. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A defensive white one-drop: 1 power and 2 toughness
/// for a single White mana; blocks profitably against most early attackers.
///
/// ## Implementation
///
/// - 1/2 <see cref="Creature"/> with <see cref="CardSubtype.Cat"/>.
/// - Mana cost {W}; <see cref="ManaCost"/>'s parser derives White from the
///   single coloured pip (CR 105.2). Mana value = 1.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Sanctuary Cat")]
public static class SanctuaryCatFactory
{
    public const string CardName = "Sanctuary Cat";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Sanctuary Cat — a vanilla {W} 1/2 Creature — Cat.
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
