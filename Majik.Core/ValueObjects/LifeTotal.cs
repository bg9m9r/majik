namespace Majik.Core.ValueObjects;

/// <summary>
/// Value object representing a player's life total.
/// Immutable and validated.
/// </summary>
public class LifeTotal : IEquatable<LifeTotal>
{
    /// <summary>
    /// The life total value.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Whether the player has lost (life total is 0 or less).
    /// </summary>
    public bool HasLost => Value <= 0;

    private LifeTotal(int value)
    {
        Value = value;
    }

    /// <summary>
    /// Create a life total with the specified value.
    /// </summary>
    public static LifeTotal Create(int value)
    {
        // Life total can be negative (player loses at 0 or less)
        // But we don't restrict it here - the domain will handle loss
        return new LifeTotal(value);
    }

    /// <summary>
    /// Default starting life total (20).
    /// </summary>
    public static LifeTotal Default => new(20);

    /// <summary>
    /// Add life to this total.
    /// </summary>
    public LifeTotal Add(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount must be non-negative", nameof(amount));
        }
        return new LifeTotal(Value + amount);
    }

    /// <summary>
    /// Subtract life from this total.
    /// </summary>
    public LifeTotal Subtract(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount must be non-negative", nameof(amount));
        }
        return new LifeTotal(Value - amount);
    }

    public bool Equals(LifeTotal? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as LifeTotal);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(LifeTotal? left, LifeTotal? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(LifeTotal? left, LifeTotal? right)
    {
        return !Equals(left, right);
    }

    public static implicit operator int(LifeTotal lifeTotal)
    {
        return lifeTotal.Value;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
