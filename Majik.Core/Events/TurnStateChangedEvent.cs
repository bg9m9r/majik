using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Fired by <c>TurnDriver.SetTurnState</c> when the turn-level (phase)
/// state transitions (TurnBeginning → PreCombatMain → Combat →
/// PostCombatMain → TurnEnding). Carries the typed
/// <see cref="TurnStateType"/> so listeners can recover which main phase
/// the game is in (CR 505).
/// </summary>
public class TurnStateChangedEvent : GameEvent
{
    public TurnStateType? PreviousState { get; }
    public TurnStateType CurrentState { get; }

    public TurnStateChangedEvent(TurnStateType? previousState, TurnStateType currentState)
        : base(EventType.PhaseStarted)
    {
        PreviousState = previousState;
        CurrentState = currentState;
    }
}
