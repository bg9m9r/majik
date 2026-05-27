using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Isamaru, Hound of Konda (Champions of Kamigawa /
/// Modern reprints, {W}).
///
/// Legendary Creature — Dog 2/2. Vanilla — no printed keywords, triggers,
/// statics, or activated abilities. One of the most efficient white weenies
/// ever printed: two power and two toughness for a single White mana, with
/// the Legendary supertype restricting copies on the battlefield (CR 704.5j).
///
/// ## Implementation
///
/// - 2/2 <see cref="Creature"/> with <see cref="CardSupertype.Legendary"/> and
///   <see cref="CardSubtype.Dog"/>.
/// - Mana cost {W}; <see cref="ManaCost"/>'s parser derives White from the
///   single coloured pip (CR 105.2). Mana value = 1.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Isamaru, Hound of Konda")]
public static class IsamaruFactory
{
    public const string CardName = "Isamaru, Hound of Konda";
    public const string PrintedManaCost = "{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Isamaru, Hound of Konda — a vanilla {W} 2/2 Legendary
    /// Creature — Dog.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Dog });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
