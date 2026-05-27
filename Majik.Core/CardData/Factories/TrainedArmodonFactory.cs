using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Trained Armodon (Odyssey / Modern reprints,
/// {1}{G}{G}).
///
/// Creature — Elephant 3/3. Vanilla — no printed keywords, triggers, statics,
/// or activated abilities. A solid green beater: 3 power and 3 toughness for
/// one generic and two Green mana (mana value 3).
///
/// ## Implementation
///
/// - 3/3 <see cref="Creature"/> with <see cref="CardSubtype.Elephant"/>.
/// - Mana cost {1}{G}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   two coloured pips (CR 105.2). Mana value = 3.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Trained Armodon")]
public static class TrainedArmodonFactory
{
    public const string CardName = "Trained Armodon";
    public const string PrintedManaCost = "{1}{G}{G}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Trained Armodon — a vanilla {1}{G}{G} 3/3 Creature — Elephant.
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
            subtypes: new[] { CardSubtype.Elephant });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
