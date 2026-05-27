using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Coral Merfolk (Alpha / Revised / Modern reprints,
/// {1}{U}).
///
/// Creature — Merfolk 2/1. Vanilla — no printed keywords, triggers, statics,
/// or activated abilities. A classic Blue weenie: 2 power and 1 toughness for
/// one generic and one Blue mana. One of the original Merfolk creatures in the
/// game.
///
/// ## Implementation
///
/// - 2/1 <see cref="Creature"/> with <see cref="CardSubtype.Merfolk"/>.
/// - Mana cost {1}{U}; <see cref="ManaCost"/>'s parser derives Blue from the
///   coloured pip (CR 105.2). Mana value = 2.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Coral Merfolk")]
public static class CoralMerfolkFactory
{
    public const string CardName = "Coral Merfolk";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Constructs Coral Merfolk — a vanilla {1}{U} 2/1 Creature — Merfolk.
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
            subtypes: new[] { CardSubtype.Merfolk });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
