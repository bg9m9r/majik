using Majik.Core.Cards;

namespace Majik.Core.Combat;

/// <summary>
/// Represents a blocking creature in combat.
/// Encapsulates blocker state and damage assignment.
/// </summary>
public class Blocker
{
    private int _assignedDamage;

    /// <summary>
    /// The creature that is blocking.
    /// </summary>
    public Creature Creature { get; }

    /// <summary>
    /// The attacker being blocked.
    /// </summary>
    public Attacker BlockedAttacker { get; }

    /// <summary>
    /// The damage assigned to this blocker.
    /// </summary>
    public int AssignedDamage
    {
        get => _assignedDamage;
        private set
        {
            if (value < 0)
            {
                throw new ArgumentException("Assigned damage cannot be negative", nameof(value));
            }
            _assignedDamage = value;
        }
    }

    /// <summary>
    /// Whether this blocker has first strike.
    /// </summary>
    public bool HasFirstStrike { get; }

    /// <summary>
    /// Whether this blocker has double strike.
    /// </summary>
    public bool HasDoubleStrike { get; }

    /// <summary>
    /// Whether this blocker has deathtouch.
    /// </summary>
    public bool HasDeathtouch { get; }

    public Blocker(Creature creature, Attacker blockedAttacker, 
        bool hasFirstStrike = false, bool hasDoubleStrike = false, bool hasDeathtouch = false)
    {
        if (creature == null)
        {
            throw new ArgumentNullException(nameof(creature));
        }

        if (blockedAttacker == null)
        {
            throw new ArgumentNullException(nameof(blockedAttacker));
        }

        Creature = creature;
        BlockedAttacker = blockedAttacker;
        HasFirstStrike = hasFirstStrike;
        HasDoubleStrike = hasDoubleStrike;
        HasDeathtouch = hasDeathtouch;
        _assignedDamage = 0;
    }

    /// <summary>
    /// Assign damage to this blocker.
    /// </summary>
    public void AssignDamage(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Damage amount cannot be negative", nameof(amount));
        }

        AssignedDamage += amount;
    }

    /// <summary>
    /// Reset damage assignment for a new damage step.
    /// </summary>
    public void ResetDamageAssignment()
    {
        AssignedDamage = 0;
    }

    /// <summary>
    /// Get the power of this blocker.
    /// </summary>
    public int GetPower()
    {
        return Creature.Power;
    }

    /// <summary>
    /// Check if this blocker can deal first strike damage.
    /// </summary>
    public bool CanDealFirstStrikeDamage()
    {
        return HasFirstStrike || HasDoubleStrike;
    }

    /// <summary>
    /// Check if this blocker can deal regular damage.
    /// </summary>
    public bool CanDealRegularDamage()
    {
        return !HasFirstStrike || HasDoubleStrike;
    }

    public override string ToString()
    {
        return $"{Creature.Name} blocking {BlockedAttacker.Creature.Name}";
    }
}
