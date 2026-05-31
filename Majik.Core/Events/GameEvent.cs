using Majik.Core.Game;

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
        // Determinism (PLAN 08 prerequisite): the event's relative ordering
        // value comes from the per-game logical clock, not wall-clock. The
        // ~25 factory delayed-trigger fences compare event timestamps
        // relatively within one resolution (e.SpawnedAt: e.Timestamp >
        // resolvedAt); keeping this monotonic per game keeps those fences
        // internally consistent on replay. Same construction order as UtcNow.
        Timestamp = LogicalClockScope.Current.NextTimestamp();
        EventId = Guid.NewGuid();
        Type = type;
    }
}
