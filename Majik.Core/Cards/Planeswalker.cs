using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents a planeswalker card/permanent.
/// </summary>
public class Planeswalker : Permanent
{
    private int _loyalty;

    /// <summary>
    /// The starting loyalty of the planeswalker.
    /// </summary>
    public int StartingLoyalty { get; }

    /// <summary>
    /// The current loyalty of the planeswalker.
    /// </summary>
    public int Loyalty
    {
        get => _loyalty;
        private set
        {
            if (value < 0)
            {
                throw new ArgumentException("Loyalty cannot be negative", nameof(value));
            }
            _loyalty = value;
        }
    }

    public Planeswalker(string name, string manaCost, int startingLoyalty, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, new[] { CardType.Planeswalker }, supertypes, subtypes)
    {
        if (startingLoyalty < 0)
        {
            throw new ArgumentException("Starting loyalty cannot be negative", nameof(startingLoyalty));
        }

        StartingLoyalty = startingLoyalty;
        Loyalty = startingLoyalty;
    }

    /// <summary>
    /// Add loyalty counters to the planeswalker.
    /// </summary>
    public void AddLoyalty(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Loyalty amount cannot be negative", nameof(amount));
        }

        Loyalty += amount;
    }

    /// <summary>
    /// Remove loyalty counters from the planeswalker.
    /// </summary>
    public void RemoveLoyalty(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Loyalty removal amount cannot be negative", nameof(amount));
        }

        Loyalty = Math.Max(0, Loyalty - amount);
    }

    /// <summary>
    /// Check if the planeswalker is dead (0 loyalty).
    /// </summary>
    public bool IsDead()
    {
        return Loyalty <= 0;
    }
}
