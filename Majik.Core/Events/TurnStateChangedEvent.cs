using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Fired by <see cref="TurnStateMachine"/> when the turn-level state
/// transitions (TurnBeginning → PreCombatMain → Combat → PostCombatMain →
/// TurnEnding). Carries the typed <see cref="TurnStateType"/> so listeners
/// can disambiguate the two main-phase steps, which the lower-level
/// <see cref="PhaseStateType"/> lumps under a single <c>Main</c> value.
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
