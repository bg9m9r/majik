using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Walking Corpse (M12 / M13 / DDQ / Modern reprints,
/// {1}{B}).
///
/// Creature — Zombie 2/2. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. A classic black vanilla: 2 power and 2 toughness for
/// one generic and one Black mana (mana value 2).
///
/// ## Implementation
///
/// - 2/2 <see cref="Creature"/> with <see cref="CardSubtype.Zombie"/>.
/// - Mana cost {1}{B}; <see cref="ManaCost"/>'s parser derives Black from the
///   single coloured pip (CR 105.2). Mana value = 2.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Walking Corpse")]
public static class WalkingCorpseFactory
{
    public const string CardName = "Walking Corpse";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Walking Corpse — a vanilla {1}{B} 2/2 Creature — Zombie.
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
            subtypes: new[] { CardSubtype.Zombie });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
