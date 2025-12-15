using Majik.Core.Cards;

namespace Majik.Core.Combat;

/// <summary>
/// Value object representing combat damage assignment.
/// Immutable and used for damage tracking during combat.
/// </summary>
public class CombatDamage : IEquatable<CombatDamage>
{
    /// <summary>
    /// The creature dealing the damage.
    /// </summary>
    public Creature Source { get; }

    /// <summary>
    /// The target receiving the damage (Creature, Player, or Planeswalker).
    /// </summary>
    public ICard? Target { get; }

    /// <summary>
    /// The amount of damage.
    /// </summary>
    public int Amount { get; }

    /// <summary>
    /// Whether this is combat damage.
    /// </summary>
    public bool IsCombatDamage { get; }

    /// <summary>
    /// Whether this damage is lethal (will kill the target creature).
    /// </summary>
    public bool IsLethal { get; }

    private CombatDamage(Creature source, ICard? target, int amount, bool isCombatDamage, bool isLethal)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (amount < 0)
        {
            throw new ArgumentException("Damage amount cannot be negative", nameof(amount));
        }

        Source = source;
        Target = target;
        Amount = amount;
        IsCombatDamage = isCombatDamage;
        IsLethal = isLethal;
    }

    /// <summary>
    /// Create combat damage to a creature.
    /// </summary>
    public static CombatDamage ToCreature(Creature source, Creature target, int amount, bool hasDeathtouch = false)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var isLethal = hasDeathtouch || amount >= target.Toughness;
        return new CombatDamage(source, target, amount, isCombatDamage: true, isLethal);
    }

    /// <summary>
    /// Create combat damage to a player.
    /// </summary>
    public static CombatDamage ToPlayer(Creature source, Players.Player target, int amount)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        return new CombatDamage(source, null, amount, isCombatDamage: true, isLethal: false);
    }

    /// <summary>
    /// Create combat damage to a planeswalker.
    /// </summary>
    public static CombatDamage ToPlaneswalker(Creature source, Planeswalker target, int amount)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var isLethal = amount >= target.Loyalty;
        return new CombatDamage(source, target, amount, isCombatDamage: true, isLethal);
    }

    public bool Equals(CombatDamage? other)
    {
        if (other == null) return false;
        return Source == other.Source &&
               Target == other.Target &&
               Amount == other.Amount &&
               IsCombatDamage == other.IsCombatDamage &&
               IsLethal == other.IsLethal;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as CombatDamage);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Source, Target, Amount, IsCombatDamage, IsLethal);
    }

    public static bool operator ==(CombatDamage? left, CombatDamage? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(CombatDamage? left, CombatDamage? right)
    {
        return !(left == right);
    }

    public override string ToString()
    {
        var targetName = Target?.Name ?? "Player";
        return $"{Source.Name} deals {Amount} damage to {targetName}";
    }
}
