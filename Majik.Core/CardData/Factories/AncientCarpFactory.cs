using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ancient Carp (Modern Horizons, {4}{U}).
///
/// Creature — Fish 2/5. Vanilla — no printed keywords, triggers, statics,
/// or activated abilities. A five-mana 2/5 beater with above-curve toughness;
/// sees fringe play in budget blue decks for its resilient blocker profile.
///
/// ## Implementation
///
/// - 2/5 <see cref="Creature"/> with <see cref="CardSubtype.Fish"/>.
/// - Mana cost {4}{U}: four generic + one blue, converted mana value 5.
///   Colour identity: blue (CR 105.2).
/// - No supertypes, no extra card types beyond Creature.
///
/// No service wiring — single-arg <see cref="Create(Player)"/> is the
/// canonical entry point.
/// </summary>
[CardName("Ancient Carp")]
public static class AncientCarpFactory
{
    public const string CardName = "Ancient Carp";
    public const string PrintedManaCost = "{4}{U}";
    public const int Power = 2;
    public const int Toughness = 5;

    /// <summary>
    /// Constructs Ancient Carp — a vanilla {4}{U} 2/5 Creature — Fish.
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
            subtypes: new[] { CardSubtype.Fish });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }
}
