using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when attackers are declared (Rule 508).
/// </summary>
public class AttackersDeclaredEvent : GameEvent
{
    public Majik.Core.Combat.Combat Combat { get; }

    public AttackersDeclaredEvent(Majik.Core.Combat.Combat combat)
        : base(EventType.AttackersDeclared)
    {
        Combat = combat ?? throw new ArgumentNullException(nameof(combat));
    }
}
