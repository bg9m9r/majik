using Majik.Core.Events;

namespace Majik.Core.StateMachine;

/// <summary>
/// Represents a game-level state (Initializing, Mulligan, Playing, GameOver).
/// </summary>
public class GameState : IState
{
    public string Name { get; }
    public GameStateType Type { get; }
    private readonly IEventBus? _eventBus;

    public GameState(GameStateType type, IEventBus? eventBus = null)
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
