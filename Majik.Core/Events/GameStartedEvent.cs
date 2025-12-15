namespace Majik.Core.Events;

/// <summary>
/// Event fired when the game starts.
/// </summary>
public class GameStartedEvent : GameEvent
{
    public GameStartedEvent() 
        : base(EventType.GameStarted)
    {
    }
}
