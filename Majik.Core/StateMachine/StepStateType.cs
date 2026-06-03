namespace Majik.Core.StateMachine;

/// <summary>
/// Enumeration of phase-level states (steps within turns).
/// </summary>
public enum StepStateType
{
    Untap,
    Upkeep,
    Draw,
    PreCombatMain,
    BeginningOfCombat,
    DeclareAttackers,
    DeclareBlockers,
    CombatDamage,
    EndOfCombat,
    PostCombatMain,
    End,
    Cleanup
}

/// <summary>
/// Extension helpers over <see cref="StepStateType"/>.
/// </summary>
public static class PhaseStateTypeExtensions
{
    /// <summary>
    /// True when the phase is either main phase (CR 505 — precombat or
    /// postcombat main). Sorcery-speed checks gate on this rather than a
    /// single raw "Main" value so callers stay agnostic to which main.
    /// </summary>
    public static bool IsMain(this StepStateType p)
        => p is StepStateType.PreCombatMain or StepStateType.PostCombatMain;
}
