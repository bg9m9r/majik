using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Roughrider (Magic 2010 / Modern reprints,
/// {2}{R}).
///
/// Creature — Goblin Knight 3/2. Vanilla — no printed keywords, triggers,
/// statics, or activated abilities. A classic red aggro body: 3 power and 2
/// toughness for two generic mana and one Red mana (mana value 3).
///
/// ## Implementation
///
/// - 3/2 <see cref="Creature"/> with <see cref="CardSubtype.Goblin"/> and
///   <see cref="CardSubtype.Knight"/>.
/// - Mana cost {2}{R}; <see cref="ManaCost"/>'s parser derives Red from the
///   single coloured pip (CR 105.2). Mana value = 3.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Goblin Roughrider")]
public static class GoblinRoughriderFactory
{
    public const string CardName = "Goblin Roughrider";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Goblin Roughrider — a vanilla {2}{R} 3/2 Creature — Goblin Knight.
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
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
