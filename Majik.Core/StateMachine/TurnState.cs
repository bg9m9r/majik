using Majik.Core.Events;

namespace Majik.Core.StateMachine;

/// <summary>
/// Represents a turn-level state.
/// </summary>
public class TurnState : IState
{
    public string Name { get; }
    public TurnStateType Type { get; }
    private readonly IEventBus? _eventBus;

    public TurnState(TurnStateType type, IEventBus? eventBus = null)
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
