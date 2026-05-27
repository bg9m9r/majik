using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alpha Tyrranax (Scars of Mirrodin, {4}{G}{G}).
///
/// Creature — Dinosaur Beast 6/5. Vanilla — no printed keywords, triggers,
/// statics, or activated abilities. A large green beater: 6 power and 5
/// toughness for six mana, carrying both the Dinosaur and Beast subtypes.
///
/// ## Implementation
///
/// - 6/5 <see cref="Creature"/> with <see cref="CardSubtype.Dinosaur"/> and
///   <see cref="CardSubtype.Beast"/>.
/// - Mana cost {4}{G}{G}; <see cref="ManaCost"/>'s parser derives Green from
///   the two coloured pips (CR 105.2). Mana value = 6.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Alpha Tyrranax")]
public static class AlphaTyrranaxFactory
{
    public const string CardName = "Alpha Tyrranax";
    public const string PrintedManaCost = "{4}{G}{G}";
    public const int Power = 6;
    public const int Toughness = 5;

    /// <summary>
    /// Constructs Alpha Tyrranax — a vanilla {4}{G}{G} 6/5 Creature — Dinosaur Beast.
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
            subtypes: new[] { CardSubtype.Dinosaur, CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
