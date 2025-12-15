using Majik.Core.Events;
using Majik.Core.Stack;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when an object is added to the stack.
/// </summary>
public class StackObjectAddedEvent : GameEvent
{
    /// <summary>
    /// The stack object that was added.
    /// </summary>
    public IStackObject StackObject { get; }

    public StackObjectAddedEvent(IStackObject stackObject) 
        : base(EventType.Triggered)
    {
        StackObject = stackObject ?? throw new ArgumentNullException(nameof(stackObject));
    }
}
