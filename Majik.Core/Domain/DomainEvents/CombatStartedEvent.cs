using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when combat begins (Rule 507).
/// </summary>
public class CombatStartedEvent : GameEvent
{
    public Player ActivePlayer { get; }

    public CombatStartedEvent(Player activePlayer)
        : base(EventType.CombatStarted)
    {
        ActivePlayer = activePlayer ?? throw new ArgumentNullException(nameof(activePlayer));
    }
}
