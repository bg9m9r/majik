namespace Majik.Core.ValueObjects;

/// <summary>
/// Value object representing a card's unique identity.
/// Immutable and used for equality comparisons.
/// </summary>
public class CardIdentity : IEquatable<CardIdentity>
{
    /// <summary>
    /// The card's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Optional set code (e.g., "M21", "ZNR").
    /// </summary>
    public string? SetCode { get; }

    /// <summary>
    /// Optional collector number.
    /// </summary>
    public string? CollectorNumber { get; }

    private CardIdentity(string name, string? setCode = null, string? collectorNumber = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Card name cannot be null or empty", nameof(name));
        }

        Name = name;
        SetCode = setCode;
        CollectorNumber = collectorNumber;
    }

    /// <summary>
    /// Create a card identity from just a name.
    /// </summary>
    public static CardIdentity FromName(string name)
    {
        return new CardIdentity(name);
    }

    /// <summary>
    /// Create a card identity with full information.
    /// </summary>
    public static CardIdentity Create(string name, string? setCode = null, string? collectorNumber = null)
    {
        return new CardIdentity(name, setCode, collectorNumber);
    }

    public bool Equals(CardIdentity? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        
        return Name == other.Name &&
               SetCode == other.SetCode &&
               CollectorNumber == other.CollectorNumber;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as CardIdentity);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, SetCode, CollectorNumber);
    }

    public static bool operator ==(CardIdentity? left, CardIdentity? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(CardIdentity? left, CardIdentity? right)
    {
        return !Equals(left, right);
    }

    public override string ToString()
    {
        if (SetCode != null && CollectorNumber != null)
        {
            return $"{Name} ({SetCode} {CollectorNumber})";
        }
        if (SetCode != null)
        {
            return $"{Name} ({SetCode})";
        }
        return Name;
    }
}
