using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alpine Grizzly (Khans of Tarkir, {2}{G}).
///
/// Creature — Bear 4/2. Vanilla — no printed keywords, triggers, statics, or
/// activated abilities. An efficient green beater: 4 power and 2 toughness
/// for two generic and one Green mana; applies pressure as an early attacker.
///
/// ## Implementation
///
/// - 4/2 <see cref="Creature"/> with <see cref="CardSubtype.Bear"/>.
/// - Mana cost {2}{G}; <see cref="ManaCost"/>'s parser derives Green from the
///   single coloured pip (CR 105.2). Mana value = 3.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Alpine Grizzly")]
public static class AlpineGrizzlyFactory
{
    public const string CardName = "Alpine Grizzly";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 4;
    public const int Toughness = 2;

    /// <summary>
    /// Constructs Alpine Grizzly — a vanilla {2}{G} 4/2 Creature — Bear.
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
            subtypes: new[] { CardSubtype.Bear });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
