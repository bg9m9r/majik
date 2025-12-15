using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents a creature card/permanent.
/// </summary>
public class Creature : Permanent
{
    private int _damage;
    private int _basePower;
    private int _baseToughness;

    /// <summary>
    /// The base power of the creature.
    /// </summary>
    public int BasePower
    {
        get => _basePower;
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("Power cannot be negative", nameof(value));
            }
            _basePower = value;
        }
    }

    /// <summary>
    /// The base toughness of the creature.
    /// </summary>
    public int BaseToughness
    {
        get => _baseToughness;
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("Toughness cannot be negative", nameof(value));
            }
            _baseToughness = value;
        }
    }

    /// <summary>
    /// The current power of the creature (base + effects).
    /// </summary>
    public int Power => GetPower();

    /// <summary>
    /// The current toughness of the creature (base + effects).
    /// </summary>
    public int Toughness => GetToughness();

    /// <summary>
    /// The damage marked on the creature.
    /// </summary>
    public int Damage
    {
        get => _damage;
        private set
        {
            if (value < 0)
            {
                throw new ArgumentException("Damage cannot be negative", nameof(value));
            }
            _damage = value;
        }
    }

    public Creature(string name, string manaCost, int power, int toughness, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, new[] { CardType.Creature }, supertypes, subtypes)
    {
        BasePower = power;
        BaseToughness = toughness;
        _damage = 0;
    }

    /// <summary>
    /// Get the current power (base + effects).
    /// For now, returns base power. Effects will be added in future.
    /// </summary>
    public int GetPower()
    {
        // TODO: Apply static effects, counters, etc.
        return BasePower;
    }

    /// <summary>
    /// Get the current toughness (base + effects).
    /// For now, returns base toughness. Effects will be added in future.
    /// </summary>
    public int GetToughness()
    {
        // TODO: Apply static effects, counters, etc.
        return BaseToughness;
    }

    /// <summary>
    /// Deal damage to the creature.
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Damage amount cannot be negative", nameof(amount));
        }

        Damage += amount;
    }

    /// <summary>
    /// Remove damage from the creature.
    /// </summary>
    public void RemoveDamage(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Removal amount cannot be negative", nameof(amount));
        }

        Damage = Math.Max(0, Damage - amount);
    }

    /// <summary>
    /// Clear all damage from the creature.
    /// </summary>
    public void ClearDamage()
    {
        Damage = 0;
    }

    /// <summary>
    /// Check if the creature is dead (damage >= toughness).
    /// </summary>
    public bool IsDead()
    {
        return Damage >= Toughness;
    }
}
