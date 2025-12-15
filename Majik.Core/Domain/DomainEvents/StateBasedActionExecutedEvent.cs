using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when a state-based action is executed.
/// </summary>
public class StateBasedActionExecutedEvent : GameEvent
{
    /// <summary>
    /// Description of the state-based action that was executed.
    /// </summary>
    public string ActionDescription { get; }

    public StateBasedActionExecutedEvent(string actionDescription) 
        : base(EventType.PhaseEnded)
    {
        ActionDescription = actionDescription ?? throw new ArgumentNullException(nameof(actionDescription));
    }
}
