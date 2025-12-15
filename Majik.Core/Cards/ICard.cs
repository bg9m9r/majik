using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Base interface for all cards.
/// </summary>
public interface ICard
{
    /// <summary>
    /// The name of the card.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The mana cost of the card.
    /// </summary>
    string ManaCost { get; }

    /// <summary>
    /// The card types (cards can have multiple types).
    /// </summary>
    IReadOnlyList<CardType> CardTypes { get; }

    /// <summary>
    /// The card supertypes.
    /// </summary>
    IReadOnlyList<CardSupertype> Supertypes { get; }

    /// <summary>
    /// The card subtypes.
    /// </summary>
    IReadOnlyList<CardSubtype> Subtypes { get; }

    /// <summary>
    /// The owner of the card.
    /// </summary>
    Player? Owner { get; set; }

    /// <summary>
    /// The current controller of the card.
    /// </summary>
    Player? Controller { get; set; }

    /// <summary>
    /// The current zone the card is in.
    /// </summary>
    ZoneType Zone { get; set; }

    /// <summary>
    /// Check if the card has a specific type.
    /// </summary>
    bool HasType(CardType type);

    /// <summary>
    /// Check if the card has a specific supertype.
    /// </summary>
    bool HasSupertype(CardSupertype supertype);

    /// <summary>
    /// Check if the card has a specific subtype.
    /// </summary>
    bool HasSubtype(CardSubtype subtype);
}
