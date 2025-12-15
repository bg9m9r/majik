using Majik.Core.Abilities;
using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Event fired when a triggered ability is triggered and placed on the stack.
/// </summary>
public class TriggeredAbilityTriggeredEvent : GameEvent
{
    public ITriggeredAbility Ability { get; }
    public GameEvent TriggeringEvent { get; }

    public TriggeredAbilityTriggeredEvent(ITriggeredAbility ability, GameEvent triggeringEvent)
        : base(EventType.TriggeredAbilityTriggered)
    {
        Ability = ability ?? throw new ArgumentNullException(nameof(ability));
        TriggeringEvent = triggeringEvent ?? throw new ArgumentNullException(nameof(triggeringEvent));
    }
}
