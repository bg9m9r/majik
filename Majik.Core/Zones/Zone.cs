using Majik.Core.Cards;

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
}
