using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Base card implementation.
/// Encapsulates card state and uses value objects.
/// </summary>
public class Card : ICard
{
    private ZoneType _zone;
    private Player? _controller;
    private readonly List<CardType> _cardTypes = new();
    private readonly List<CardSupertype> _supertypes = new();
    private readonly List<CardSubtype> _subtypes = new();

    public string Name { get; }
    public string ManaCost { get; }
    
    /// <summary>
    /// The mana cost as a value object.
    /// </summary>
    public ValueObjects.ManaCost ManaCostValue { get; }

    /// <summary>
    /// The card types (cards can have multiple types).
    /// </summary>
    public IReadOnlyList<CardType> CardTypes => _cardTypes.AsReadOnly();

    /// <summary>
    /// The card supertypes.
    /// </summary>
    public IReadOnlyList<CardSupertype> Supertypes => _supertypes.AsReadOnly();

    /// <summary>
    /// The card subtypes.
    /// </summary>
    public IReadOnlyList<CardSubtype> Subtypes => _subtypes.AsReadOnly();
    
    public Player? Owner { get; set; }
    
    public Player? Controller
    {
        get => _controller;
        set => _controller = value;
    }
    
    public ZoneType Zone
    {
        get => _zone;
        set => _zone = value;
    }

    public Card(string name, string manaCost = "", IEnumerable<CardType>? cardTypes = null, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Card name cannot be null or empty", nameof(name));
        }

        Name = name;
        ManaCost = manaCost;
        ManaCostValue = ValueObjects.ManaCost.Parse(manaCost);
        _zone = ZoneType.Library;
        _controller = null;

        if (cardTypes != null)
        {
            _cardTypes.AddRange(cardTypes);
        }

        if (supertypes != null)
        {
            _supertypes.AddRange(supertypes);
        }

        if (subtypes != null)
        {
            _subtypes.AddRange(subtypes);
        }
    }

    public bool HasType(CardType type)
    {
        return _cardTypes.Contains(type);
    }

    public bool HasSupertype(CardSupertype supertype)
    {
        return _supertypes.Contains(supertype);
    }

    public bool HasSubtype(CardSubtype subtype)
    {
        return _subtypes.Contains(subtype);
    }

    public override string ToString()
    {
        return Name;
    }
}
