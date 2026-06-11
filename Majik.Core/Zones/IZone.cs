using Majik.Core.Cards;
using Majik.Core.Random;

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
    /// NO-COPY read-only view of this zone's cards, in zone order.
    /// <see cref="GetCards"/> returns a defensive copy per call — correct for
    /// general consumers, but measurably wasteful on per-iteration bot-search
    /// paths (the sandbox cloner walks every zone of every player on every
    /// MCTS iteration). Callers of this view MUST NOT add/remove zone cards
    /// while enumerating it. Default implementation falls back to the copying
    /// path so existing implementers stay correct without changes.
    /// </summary>
    IReadOnlyList<ICard> CardsView => GetCards().ToList();

    /// <summary>
    /// Clear all cards from this zone.
    /// </summary>
    void Clear();

    /// <summary>
    /// Insert a card at a specific index. Used for "put on top of
    /// library" (index 0) and similar ordered-zone effects. Default
    /// implementation appends to the end; ordered zones (Library,
    /// Stack) override.
    /// </summary>
    void InsertCardAt(int index, ICard card)
    {
        AddCard(card);
    }

    /// <summary>
    /// CR 701.20 — randomize the order of cards in this zone in place.
    /// Used for library shuffles after search effects (CR 701.20a) and
    /// for the initial pre-game library shuffle (CR 103.1). The
    /// supplied <see cref="GameRandom"/> drives Fisher-Yates so
    /// deterministic replay is preserved when callers thread a seeded
    /// RNG.
    ///
    /// Default implementation is a no-op for zones whose order is not
    /// meaningful (Battlefield, Hand viewed by owner, Graveyard — the
    /// latter is ordered but is never "shuffled" by any rule). Ordered
    /// zones with hidden information (Library) override.
    /// </summary>
    void Shuffle(GameRandom random)
    {
        // No-op default: most zones are not order-sensitive.
    }
}
