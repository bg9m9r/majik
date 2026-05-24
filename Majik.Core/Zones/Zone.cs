using Majik.Core.Cards;
using Majik.Core.Random;

namespace Majik.Core.Zones;

/// <summary>
/// Generic zone implementation.
/// Manages a collection of cards.
/// </summary>
public class Zone : IZone
{
    private readonly List<ICard> _cards = new();

    public ZoneType Type { get; }
    public string Name { get; }
    public int Count => _cards.Count;

    public Zone(ZoneType type, string name)
    {
        Type = type;
        Name = name;
    }

    public void AddCard(ICard card)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        if (!_cards.Contains(card))
        {
            _cards.Add(card);
            // Update card's zone property
            card.SetZone(Type);
        }
    }

    public bool RemoveCard(ICard card)
    {
        return _cards.Remove(card);
    }

    public bool ContainsCard(ICard card)
    {
        return _cards.Contains(card);
    }

    public IEnumerable<ICard> GetCards()
    {
        return _cards.ToList();
    }

    public void Clear()
    {
        _cards.Clear();
    }

    public void InsertCardAt(int index, ICard card)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (_cards.Contains(card)) return;
        var clamped = Math.Clamp(index, 0, _cards.Count);
        _cards.Insert(clamped, card);
        card.SetZone(Type);
    }

    /// <summary>
    /// CR 701.20 — Fisher-Yates shuffle of the underlying card list in
    /// place. <paramref name="random"/> is required so deterministic
    /// replay is preserved (seeded RNG → identical sequence).
    ///
    /// We only shuffle when ordering is meaningful — Library is the
    /// canonical caller; Battlefield / Hand / Graveyard are spec'd to
    /// no-op on shuffle but the operation is still safe (it just
    /// re-orders an order-insensitive collection).
    /// </summary>
    public void Shuffle(GameRandom random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        if (_cards.Count < 2) return;
        random.Shuffle(_cards);
    }
}
