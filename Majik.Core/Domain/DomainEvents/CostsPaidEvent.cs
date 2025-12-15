using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Stack;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when costs are paid for a spell or ability.
/// </summary>
public class CostsPaidEvent : GameEvent
{
    /// <summary>
    /// The stack object (spell or ability) that had costs paid.
    /// </summary>
    public IStackObject StackObject { get; }

    /// <summary>
    /// The costs that were paid.
    /// </summary>
    public IReadOnlyList<ICost> Costs { get; }

    public CostsPaidEvent(IStackObject stackObject, IEnumerable<ICost> costs) 
        : base(EventType.Triggered)
    {
        StackObject = stackObject ?? throw new ArgumentNullException(nameof(stackObject));
        Costs = costs?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(costs));
    }
}
