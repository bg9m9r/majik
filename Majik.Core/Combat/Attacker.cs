using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Combat;

/// <summary>
/// Represents an attacking creature in combat.
/// Encapsulates attacker state and damage assignment.
/// </summary>
public class Attacker
{
    private readonly List<Blocker> _blockers = new();
    private int _assignedDamage;

    /// <summary>
    /// The creature that is attacking.
    /// </summary>
    public Creature Creature { get; }

    /// <summary>
    /// The player being attacked (if attacking player).
    /// </summary>
    public Player? TargetPlayer { get; }

    /// <summary>
    /// The planeswalker being attacked (if attacking planeswalker).
    /// </summary>
    public Planeswalker? TargetPlaneswalker { get; }

    /// <summary>
    /// The creatures blocking this attacker.
    /// </summary>
    public IReadOnlyList<Blocker> Blockers => _blockers.AsReadOnly();

    /// <summary>
    /// The total damage assigned by this attacker.
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
    /// Whether this attacker has first strike.
    /// </summary>
    public bool HasFirstStrike { get; }

    /// <summary>
    /// Whether this attacker has double strike.
    /// </summary>
    public bool HasDoubleStrike { get; }

    /// <summary>
    /// Whether this attacker has trample.
    /// </summary>
    public bool HasTrample { get; }

    /// <summary>
    /// Whether this attacker has deathtouch.
    /// </summary>
    public bool HasDeathtouch { get; }

    /// <summary>
    /// Whether this attacker has vigilance.
    /// </summary>
    public bool HasVigilance { get; }

    public Attacker(Creature creature, Player? targetPlayer = null, Planeswalker? targetPlaneswalker = null, 
        bool hasFirstStrike = false, bool hasDoubleStrike = false, bool hasTrample = false, 
        bool hasDeathtouch = false, bool hasVigilance = false)
    {
        if (creature == null)
        {
            throw new ArgumentNullException(nameof(creature));
        }

        if (targetPlayer == null && targetPlaneswalker == null)
        {
            throw new ArgumentException("Must target either a player or planeswalker", nameof(targetPlayer));
        }

        if (targetPlayer != null && targetPlaneswalker != null)
        {
            throw new ArgumentException("Cannot target both player and planeswalker", nameof(targetPlayer));
        }

        Creature = creature;
        TargetPlayer = targetPlayer;
        TargetPlaneswalker = targetPlaneswalker;
        HasFirstStrike = hasFirstStrike;
        HasDoubleStrike = hasDoubleStrike;
        HasTrample = hasTrample;
        HasDeathtouch = hasDeathtouch;
        HasVigilance = hasVigilance;
        _assignedDamage = 0;
    }

    /// <summary>
    /// Add a blocker to this attacker.
    /// </summary>
    public void AddBlocker(Blocker blocker)
    {
        if (blocker == null)
        {
            throw new ArgumentNullException(nameof(blocker));
        }

        if (blocker.BlockedAttacker != this)
        {
            throw new ArgumentException("Blocker does not block this attacker", nameof(blocker));
        }

        if (_blockers.Contains(blocker))
        {
            return; // Already blocking
        }

        _blockers.Add(blocker);
    }

    /// <summary>
    /// Assign damage from this attacker.
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
    /// Get the power of this attacker.
    /// </summary>
    public int GetPower()
    {
        return Creature.Power;
    }

    /// <summary>
    /// Check if this attacker can deal first strike damage.
    /// </summary>
    public bool CanDealFirstStrikeDamage()
    {
        return HasFirstStrike || HasDoubleStrike;
    }

    /// <summary>
    /// Check if this attacker can deal regular damage.
    /// </summary>
    public bool CanDealRegularDamage()
    {
        return !HasFirstStrike || HasDoubleStrike;
    }

    public override string ToString()
    {
        var target = TargetPlayer?.Name ?? TargetPlaneswalker?.Name ?? "Unknown";
        return $"{Creature.Name} attacking {target}";
    }
}
