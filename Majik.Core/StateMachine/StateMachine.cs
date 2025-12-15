namespace Majik.Core.StateMachine;

/// <summary>
/// Generic state machine implementation.
/// Manages state transitions and state lifecycle.
/// </summary>
/// <typeparam name="TState">The type of states this machine manages.</typeparam>
public class StateMachine<TState> where TState : class, IState
{
    private TState? _currentState;
    private readonly Dictionary<string, TState> _states = new();

    /// <summary>
    /// The current state.
    /// </summary>
    public TState? CurrentState => _currentState;

    /// <summary>
    /// Event fired when a state transition occurs.
    /// </summary>
    public event Action<TState?, TState>? StateChanged;

    /// <summary>
    /// Register a state with the state machine.
    /// </summary>
    public void RegisterState(TState state)
    {
        _states[state.Name] = state;
    }

    /// <summary>
    /// Transition to a new state by name.
    /// </summary>
    /// <param name="nextState">The state to transition to.</param>
    /// <returns>True if the transition was successful, false otherwise.</returns>
    public bool TransitionTo(TState nextState)
    {

        var previousState = _currentState;
        
        previousState?.OnExit();
        _currentState = nextState;
        _currentState.OnEnter();
        
        StateChanged?.Invoke(previousState, _currentState);
        
        return true;
    }

    /// <summary>
    /// Update the current state.
    /// </summary>
    public void Update()
    {
        _currentState?.OnUpdate();
    }
}
