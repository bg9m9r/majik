namespace Majik.Core.StateMachine;

/// <summary>
/// Enumeration of phase-level states (steps within turns).
/// </summary>
public enum PhaseStateType
{
    Untap,
    Upkeep,
    Draw,
    Main,
    BeginningOfCombat,
    DeclareAttackers,
    DeclareBlockers,
    CombatDamage,
    EndOfCombat,
    End,
    Cleanup
}
