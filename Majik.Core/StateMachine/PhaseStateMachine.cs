using Majik.Core.Events;

namespace Majik.Core.StateMachine;

/// <summary>
/// Phase-level state machine.
/// Manages individual phases/steps within turns.
/// </summary>
public class PhaseStateMachine : StateMachine<PhaseState>
{
    private readonly IEventBus? _eventBus;

    public PhaseStateMachine(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
        
        // Register default phase states
        RegisterState(new PhaseState(PhaseStateType.Untap, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.Upkeep, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.Draw, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.Main, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.BeginningOfCombat, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.DeclareAttackers, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.DeclareBlockers, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.CombatDamage, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.EndOfCombat, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.End, _eventBus));
        RegisterState(new PhaseState(PhaseStateType.Cleanup, _eventBus));
        
        // Subscribe to state changes
        StateChanged += OnStateChanged;
    }

    private void OnStateChanged(PhaseState? previous, PhaseState current)
    {
        _eventBus?.Publish(new PhaseChangedEvent(previous?.Name, current.Name));
    }
}
