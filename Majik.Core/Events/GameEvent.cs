namespace Majik.Core.Events;

/// <summary>
/// Base class for all game events.
/// All game actions emit events that can be subscribed to by any UI implementation.
/// </summary>
public abstract class GameEvent
{
    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    public Guid EventId { get; }

    /// <summary>
    /// Type of event.
    /// </summary>
    public EventType Type { get; }

    protected GameEvent(EventType type)
    {
        Timestamp = DateTime.UtcNow;
        EventId = Guid.NewGuid();
        Type = type;
    }
}
