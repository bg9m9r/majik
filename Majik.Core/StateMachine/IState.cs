namespace Majik.Core.StateMachine;

/// <summary>
/// Base interface for all states in the state machine.
/// </summary>
public interface IState
{
    /// <summary>
    /// Name of the state.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Called when entering this state.
    /// </summary>
    void OnEnter();

    /// <summary>
    /// Called when exiting this state.
    /// </summary>
    void OnExit();

    /// <summary>
    /// Called each update cycle while in this state.
    /// </summary>
    void OnUpdate();
}
