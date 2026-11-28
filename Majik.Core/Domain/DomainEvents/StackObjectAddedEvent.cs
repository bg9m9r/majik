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

    /// <summary>
    /// The stack entry's id (see <see cref="Majik.Core.Stack.Stack.GetEntryIds"/>):
    /// differs from <see cref="IStackObject.Id"/> only for a repeat firing of a
    /// triggered ability (CR 603.3). Null when the publisher didn't supply it.
    /// </summary>
    public Guid? EntryId { get; init; }

    public StackObjectAddedEvent(IStackObject stackObject) 
        : base()
    {
        StackObject = stackObject ?? throw new ArgumentNullException(nameof(stackObject));
    }
}
