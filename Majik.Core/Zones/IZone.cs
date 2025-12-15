using Majik.Core.Cards;

namespace Majik.Core.Zones;

/// <summary>
/// Base interface for zones.
/// Zones are containers for cards.
/// </summary>
public interface IZone
{
    /// <summary>
    /// The type of zone.
    /// </summary>
    ZoneType Type { get; }

    /// <summary>
    /// The name of the zone.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Number of cards in this zone.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Add a card to this zone.
    /// </summary>
    void AddCard(ICard card);

    /// <summary>
    /// Remove a card from this zone.
    /// </summary>
    bool RemoveCard(ICard card);

    /// <summary>
    /// Check if this zone contains a card.
    /// </summary>
    bool ContainsCard(ICard card);

    /// <summary>
    /// Get all cards in this zone.
    /// </summary>
    IEnumerable<ICard> GetCards();

    /// <summary>
    /// Clear all cards from this zone.
    /// </summary>
    void Clear();
}
