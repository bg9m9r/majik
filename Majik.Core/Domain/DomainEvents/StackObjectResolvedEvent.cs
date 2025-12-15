using Majik.Core.Events;
using Majik.Core.Stack;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when an object resolves from the stack.
/// </summary>
public class StackObjectResolvedEvent : GameEvent
{
    /// <summary>
    /// The stack object that resolved.
    /// </summary>
    public IStackObject StackObject { get; }

    public StackObjectResolvedEvent(IStackObject stackObject) 
        : base(EventType.Resolved)
    {
        StackObject = stackObject ?? throw new ArgumentNullException(nameof(stackObject));
    }
}
