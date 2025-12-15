using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// Event fired when a turn ends.
/// </summary>
public class TurnEndedEvent : GameEvent
{
    /// <summary>
    /// The player whose turn just ended.
    /// </summary>
    public Player Player { get; }

    /// <summary>
    /// The turn number that just ended.
    /// </summary>
    public int TurnNumber { get; }

    public TurnEndedEvent(Player player, int turnNumber) 
        : base(EventType.TurnEnded)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        TurnNumber = turnNumber;
    }
}
