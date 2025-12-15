using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when an extra turn is added to the queue.
/// </summary>
public class ExtraTurnAddedEvent : GameEvent
{
    /// <summary>
    /// The player who will take the extra turn.
    /// </summary>
    public Player Player { get; }

    public ExtraTurnAddedEvent(Player player) 
        : base(EventType.TurnStarted)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
