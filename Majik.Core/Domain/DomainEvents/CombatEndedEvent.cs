using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when combat ends (Rule 511).
/// </summary>
public class CombatEndedEvent : GameEvent
{
    public Majik.Core.Combat.Combat Combat { get; }

    public CombatEndedEvent(Majik.Core.Combat.Combat combat)
        : base(EventType.CombatEnded)
    {
        Combat = combat ?? throw new ArgumentNullException(nameof(combat));
    }
}
