using Majik.Core.Events;

namespace Majik.Core.StateMachine;

/// <summary>
/// Represents a phase-level state (step within a turn).
/// </summary>
public class PhaseState : IState
{
    public string Name { get; }
    public PhaseStateType Type { get; }
    private readonly IEventBus? _eventBus;

    public PhaseState(PhaseStateType type, IEventBus? eventBus = null)
    {
        Type = type;
        Name = type.ToString();
        _eventBus = eventBus;
    }

    public virtual void OnEnter()
    {
        // Override in derived classes if needed
    }

    public virtual void OnExit()
    {
        // Override in derived classes if needed
    }

    public virtual void OnUpdate()
    {
        // Override in derived classes if needed
    }
}
