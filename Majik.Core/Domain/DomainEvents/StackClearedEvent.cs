using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when the stack is cleared.
/// </summary>
public class StackClearedEvent : GameEvent
{
    public StackClearedEvent() 
        : base(EventType.PhaseEnded)
    {
    }
}
