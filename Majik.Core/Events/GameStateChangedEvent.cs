using Majik.Core.StateMachine;

namespace Majik.Core.Events;

/// <summary>
/// Fired when the top-level game
/// lifecycle state transitions (Initializing → Mulligan → Playing →
/// GameOver). Carries the typed <see cref="GameStateType"/>.
/// <para>
/// This is the game-lifecycle channel, deliberately distinct from the
/// phase / step channel (<see cref="PhaseStateChangedEvent"/>,
/// <see cref="StepStartedEvent"/>). It
/// replaced the old multiplexed <c>PhaseChangedEvent</c> emit so
/// lifecycle names ("Mulligan", "Playing") never leak into the UI's phase
/// label.
/// </para>
/// </summary>
public class GameStateChangedEvent : GameEvent
{
    public GameStateType? PreviousState { get; }
    public GameStateType CurrentState { get; }

    public GameStateChangedEvent(GameStateType? previousState, GameStateType currentState)
        : base(EventType.GameStateChanged)
    {
        PreviousState = previousState;
        CurrentState = currentState;
    }
}
