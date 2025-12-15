using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when a player passes priority.
/// </summary>
public class PriorityPassedEvent : GameEvent
{
    /// <summary>
    /// The player who passed priority.
    /// </summary>
    public Player Player { get; }

    public PriorityPassedEvent(Player player) 
        : base(EventType.PhaseEnded)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
