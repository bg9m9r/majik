using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Onakke Ogre (Magic 2013 / Modern reprints,
/// {2}{R}).
///
/// Creature — Ogre Warrior 4/2. Vanilla — no printed keywords, triggers,
/// statics, or activated abilities. A classic red aggro body: 4 power and 2
/// toughness for two generic mana and one Red mana (mana value 3).
///
/// ## Implementation
///
/// - 4/2 <see cref="Creature"/> with <see cref="CardSubtype.Ogre"/> and
///   <see cref="CardSubtype.Warrior"/>.
/// - Mana cost {2}{R}; <see cref="ManaCost"/>'s parser derives Red from the
///   single coloured pip (CR 105.2). Mana value = 3.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Onakke Ogre")]
public static class OnakkeOgreFactory
{
    public const string CardName = "Onakke Ogre";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 4;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Onakke Ogre — a vanilla {2}{R} 4/2 Creature — Ogre Warrior.
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
            subtypes: new[] { CardSubtype.Ogre, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
