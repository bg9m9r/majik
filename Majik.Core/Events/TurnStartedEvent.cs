using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a turn starts.
/// </summary>
public class TurnStartedEvent : GameEvent
{
    /// <summary>
    /// The player whose turn it is.
    /// </summary>
    public Player Player { get; }

    /// <summary>
    /// The turn number.
    /// </summary>
    public int TurnNumber { get; }

    public TurnStartedEvent(Player player, int turnNumber) 
        : base(EventType.TurnStarted)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        TurnNumber = turnNumber;
    }
}
