using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nessian Courser (Theros / Modern reprints, {2}{G}).
///
/// Creature — Centaur Warrior 3/3. Vanilla — no printed keywords, triggers,
/// statics, or activated abilities. A solid green body: 3 power and 3
/// toughness for two generic mana and one Green mana (mana value 3).
///
/// ## Implementation
///
/// - 3/3 <see cref="Creature"/> with <see cref="CardSubtype.Centaur"/> and
///   <see cref="CardSubtype.Warrior"/>.
/// - Mana cost {2}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   single coloured pip (CR 105.2). Mana value = 3.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Nessian Courser")]
public static class NessianCourserFactory
{
    public const string CardName = "Nessian Courser";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Nessian Courser — a vanilla {2}{G} 3/3 Creature — Centaur Warrior.
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
            subtypes: new[] { CardSubtype.Centaur, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
