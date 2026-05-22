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
    /// Optional reference to the <see cref="Majik.Core.Effects.ContinuousEffectsService"/>
    /// that owns this creature. When set, P/T and keyword lookups consult
    /// it (CR 613 layer system). When null, base values are returned.
    /// </summary>
    public Majik.Core.Effects.ContinuousEffectsService? ActiveEffects { get; set; }

    /// <summary>Get the current power after applying continuous effects.</summary>
    public int GetPower()
    {
        if (ActiveEffects == null) return BasePower;
        return ActiveEffects.Compute(this).Power;
    }

    /// <summary>Get the current toughness after applying continuous effects.</summary>
    public int GetToughness()
    {
        if (ActiveEffects == null) return BaseToughness;
        return ActiveEffects.Compute(this).Toughness;
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

    /// <summary>
    /// CR 702.2b — set by combat when a deathtouch source deals nonzero
    /// damage to this creature. The SBA pass uses this as a synonym for
    /// "lethal damage marked." Cleared in cleanup along with Damage.
    /// </summary>
    public bool MarkedForDestructionByDeathtouch { get; set; }

    /// <summary>CR 903.3 — flagged if this creature is its controller's
    /// commander. Combat damage from a commander is tracked per-opponent
    /// for the 21-damage loss condition (CR 903.10a).</summary>
    public bool IsCommander { get; set; }

    /// <summary>
    /// CR 702.74b — set by <see cref="Majik.Core.Costs.EvokeAlternativeCost"/>
    /// when the spell was cast for its evoke cost. The "sacrifice this
    /// creature" ETB trigger added to evoke creatures (CR 702.74c) reads
    /// this as an intervening-if to decide whether to fire. Tagged on the
    /// Creature object so it survives the Stack → Battlefield transition
    /// (we set it during the alt-cost's <c>OnResolved</c>, which runs
    /// before <see cref="Majik.Core.Services.StackResolver"/> moves the
    /// card to the battlefield and fires the ETB <see cref="CardMovedEvent"/>).
    /// </summary>
    public bool EvokeWasPaid { get; set; }
}
