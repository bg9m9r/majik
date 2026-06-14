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
    /// Create combat damage to a planeswalker. CR 711 Option B: the defender is
    /// any permanent that is EFFECTIVELY a planeswalker — a real
    /// <see cref="Planeswalker"/> OR a non-planeswalker permanent carrying a
    /// transient loyalty body (an animated / flipped-DFC planeswalker back). The
    /// signature is widened from <see cref="Planeswalker"/> to
    /// <see cref="Permanent"/> to match the live damage path's
    /// <c>DamageIntent.TargetPlaneswalker</c> / <c>CombatManager.AttackerDeclaration</c>
    /// widenings, both of which already gate on
    /// <see cref="Permanent.IsEffectivePlaneswalker"/> and route lethality
    /// through <see cref="Permanent.GetEffectiveLoyalty"/> /
    /// <see cref="Permanent.RemoveTransientLoyalty"/>.
    /// </summary>
    public static CombatDamage ToPlaneswalker(Creature source, Permanent target, int amount)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (!target.IsEffectivePlaneswalker())
        {
            throw new ArgumentException(
                "Target is not an effective planeswalker (no loyalty body)", nameof(target));
        }

        // CR 306.5b / 120.3 — lethal when combat damage meets the EFFECTIVE
        // loyalty (transient body for a flipped/animated back, authoritative
        // loyalty for a real planeswalker via the GetEffectiveLoyalty override).
        var isLethal = amount >= target.GetEffectiveLoyalty()!.Value;
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
