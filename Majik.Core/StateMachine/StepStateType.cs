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

    /// <summary>
    /// Maps a fine-grained <see cref="StepStateType"/> (step-level) to its
    /// coarse <see cref="PhaseStateType"/> (phase-level). Used when building
    /// a <c>SimState</c> from a live <c>GameContext.CurrentPhase</c>.
    /// </summary>
    public static PhaseStateType ToPhaseStateType(this StepStateType step) => step switch
    {
        StepStateType.Untap
            or StepStateType.Upkeep
            or StepStateType.Draw       => PhaseStateType.TurnBeginning,
        StepStateType.PreCombatMain     => PhaseStateType.PreCombatMain,
        StepStateType.BeginningOfCombat
            or StepStateType.DeclareAttackers
            or StepStateType.DeclareBlockers
            or StepStateType.CombatDamage
            or StepStateType.EndOfCombat => PhaseStateType.Combat,
        StepStateType.PostCombatMain    => PhaseStateType.PostCombatMain,
        StepStateType.End
            or StepStateType.Cleanup    => PhaseStateType.TurnEnding,
        _                               => PhaseStateType.PreCombatMain,
    };
}
