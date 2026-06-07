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

    /// <summary>Simulation copy constructor. Chains through
    /// <see cref="Permanent(Permanent)"/> for base + Permanent runtime state,
    /// then copies <see cref="Planeswalker"/>-specific runtime state (current
    /// loyalty, which may differ from <see cref="StartingLoyalty"/>).
    /// </summary>
    protected Planeswalker(Planeswalker src) : base(src)
    {
        // preserves: StartingLoyalty (definition), _loyalty (runtime — current loyalty may differ)
        StartingLoyalty = src.StartingLoyalty;
        _loyalty = src._loyalty;
    }

    /// <inheritdoc cref="Card.CloneForSim"/>
    internal override Card CloneForSim() => new Planeswalker(this);

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

    /// <summary>
    /// CR 306.5b — a real planeswalker's effective loyalty is its own
    /// authoritative <see cref="Loyalty"/> field, NOT the transient surface
    /// (which exists for creature-front transform DFC backs). Overrides the
    /// <see cref="Permanent.GetEffectiveLoyalty"/> default so the loyalty
    /// subsystem reads one value for both shapes.
    /// </summary>
    public override int? GetEffectiveLoyalty() => Loyalty;

    /// <summary>
    /// CR 306.7 — loyalty removal on a real planeswalker routes to its own
    /// field (not the transient surface).
    /// </summary>
    public override bool RemoveTransientLoyalty(int amount)
    {
        RemoveLoyalty(amount);
        return true;
    }
}
