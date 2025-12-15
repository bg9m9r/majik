using Majik.Core.Events;
using Majik.Core.Stack;
using Majik.Core.Targeting;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when targets are chosen for a spell or ability.
/// </summary>
public class TargetsChosenEvent : GameEvent
{
    /// <summary>
    /// The stack object (spell or ability) that has targets.
    /// </summary>
    public IStackObject StackObject { get; }

    /// <summary>
    /// The targets that were chosen.
    /// </summary>
    public IReadOnlyList<ITarget> Targets { get; }

    public TargetsChosenEvent(IStackObject stackObject, IEnumerable<ITarget> targets) 
        : base(EventType.Triggered)
    {
        StackObject = stackObject ?? throw new ArgumentNullException(nameof(stackObject));
        Targets = targets?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(targets));
    }
}
