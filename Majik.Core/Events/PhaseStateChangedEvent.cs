using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Fired by <c>TurnDriver.SetTurnState</c> when the turn-level (phase)
/// state transitions (TurnBeginning → PreCombatMain → Combat →
/// PostCombatMain → TurnEnding). Carries the typed
/// <see cref="PhaseStateType"/> so listeners can recover which main phase
/// the game is in (CR 505).
/// </summary>
public class PhaseStateChangedEvent : GameEvent
{
    public PhaseStateType? PreviousState { get; }
    public PhaseStateType CurrentState { get; }

    public PhaseStateChangedEvent(PhaseStateType? previousState, PhaseStateType currentState)
        : base(EventType.PhaseStarted)
    {
        PreviousState = previousState;
        CurrentState = currentState;
    }
}
