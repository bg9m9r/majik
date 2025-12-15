using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when a player receives priority.
/// </summary>
public class PriorityReceivedEvent : GameEvent
{
    /// <summary>
    /// The player who received priority.
    /// </summary>
    public Player Player { get; }

    public PriorityReceivedEvent(Player player) 
        : base(EventType.PhaseStarted)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
