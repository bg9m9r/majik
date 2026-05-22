namespace Majik.Core.Effects;

/// <summary>
/// Combat-side per-turn restrictions registered on
/// <see cref="ContinuousEffectsService"/> and consulted by the combat
/// validator. The set is intentionally tight — only what concrete spell
/// templates install today.
/// </summary>
public enum CombatRestriction
{
    /// <summary>CR 509.1c — creature cannot be declared as a blocker
    /// (Falter, Magmatic Chasm, Ground Rift, target-creature variants).</summary>
    CannotBlock,

    /// <summary>CR 508.1c — creature cannot be declared as an attacker
    /// (Pacifism-like spells, Orim's Chant rider, "can't attack this turn").</summary>
    CannotAttack,

    /// <summary>CR 702.x — attacker cannot be blocked at all this turn
    /// (Slip Through Space, Trailblazer, evasion grants).</summary>
    CannotBeBlocked,
}
