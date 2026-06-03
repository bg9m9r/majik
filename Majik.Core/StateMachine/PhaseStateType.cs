namespace Majik.Core.StateMachine;

/// <summary>
/// Enumeration of turn-level states.
/// </summary>
public enum PhaseStateType
{
    TurnBeginning,
    PreCombatMain,
    Combat,
    PostCombatMain,
    TurnEnding
}
