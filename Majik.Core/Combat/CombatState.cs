namespace Majik.Core.Combat;

/// <summary>
/// Represents the current state of a combat instance.
/// </summary>
public enum CombatState
{
    /// <summary>
    /// Combat has started, waiting for attacker declaration.
    /// </summary>
    DeclaringAttackers,

    /// <summary>
    /// Attackers declared, waiting for blocker declaration.
    /// </summary>
    DeclaringBlockers,

    /// <summary>
    /// Blockers declared, assigning damage.
    /// </summary>
    AssigningDamage,

    /// <summary>
    /// Damage assigned, resolving damage.
    /// </summary>
    ResolvingDamage,

    /// <summary>
    /// Combat has ended.
    /// </summary>
    Resolved
}
