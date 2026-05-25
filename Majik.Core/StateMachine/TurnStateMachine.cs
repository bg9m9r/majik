using Majik.Core.Events;

namespace Majik.Core.StateMachine;

/// <summary>
/// Turn-level state machine.
/// Manages turn states: TurnBeginning, PreCombatMain, Combat, PostCombatMain, TurnEnding.
/// </summary>
public class TurnStateMachine : StateMachine<TurnState>
{
    private readonly IEventBus? _eventBus;

    public TurnStateMachine(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
        
        // Register default turn states
        RegisterState(new TurnState(TurnStateType.TurnBeginning, _eventBus));
        RegisterState(new TurnState(TurnStateType.PreCombatMain, _eventBus));
        RegisterState(new TurnState(TurnStateType.Combat, _eventBus));
        RegisterState(new TurnState(TurnStateType.PostCombatMain, _eventBus));
        RegisterState(new TurnState(TurnStateType.TurnEnding, _eventBus));
        
        // Subscribe to state changes
        StateChanged += OnStateChanged;
    }

    private void OnStateChanged(TurnState? previous, TurnState current)
    {
        _eventBus?.Publish(new PhaseChangedEvent(previous?.Name, current.Name));
        // Typed companion event. PhaseStateMachine fires its own
        // PhaseChangedEvent with a different vocabulary ("Main", "Untap",
        // …), so the string-only event isn't enough to recover which
        // turn-state we're inside. Downstream wire code (GameFacade +
        // EventPayloadBuilder) tracks this typed event to disambiguate
        // PhaseStateType.Main into PreCombatMain / PostCombatMain at the
        // serialization boundary.
        _eventBus?.Publish(new TurnStateChangedEvent(previous?.Type, current.Type));
    }
}
