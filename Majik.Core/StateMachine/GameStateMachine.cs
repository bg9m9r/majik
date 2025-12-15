using Majik.Core.Events;

namespace Majik.Core.StateMachine;

/// <summary>
/// Top-level game state machine.
/// Manages high-level game states: Initializing, Mulligan, Playing, GameOver.
/// </summary>
public class GameStateMachine : StateMachine<GameState>
{
    private readonly IEventBus? _eventBus;
    private readonly Dictionary<GameStateType, GameState> _statesByType = new();

    public GameStateMachine(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
        
        // Register default states
        RegisterState(new GameState(GameStateType.Initializing, _eventBus));
        RegisterState(new GameState(GameStateType.Mulligan, _eventBus));
        RegisterState(new GameState(GameStateType.Playing, _eventBus));
        RegisterState(new GameState(GameStateType.GameOver, _eventBus));
        
        // Subscribe to state changes
        StateChanged += OnStateChanged;
        
        // Start in Initializing state
        var initialState = GetState(GameStateType.Initializing);
        if (initialState != null)
        {
            TransitionTo(initialState);
        }
    }

    /// <summary>
    /// Register a state with the state machine (also stores by type).
    /// </summary>
    public new void RegisterState(GameState state)
    {
        base.RegisterState(state);
        _statesByType[state.Type] = state;
    }

    /// <summary>
    /// Get a state by enum type (efficient, no string conversion).
    /// </summary>
    public GameState? GetState(GameStateType stateType)
    {
        _statesByType.TryGetValue(stateType, out var state);
        return state;
    }

    /// <summary>
    /// Transition to a state by enum type.
    /// </summary>
    public bool TransitionTo(GameStateType stateType)
    {
        var state = GetState(stateType);
        if (state == null)
        {
            return false;
        }
        return TransitionTo(state);
    }

    private void OnStateChanged(GameState? previous, GameState current)
    {
        _eventBus?.Publish(new PhaseChangedEvent(previous?.Name, current.Name));
    }
}
