using Majik.Core.Abilities;
using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Domain event fired when an ability is activated.
/// </summary>
public class AbilityActivatedEvent : GameEvent
{
    /// <summary>
    /// The ability that was activated.
    /// </summary>
    public IActivatedAbility Ability { get; }

    public AbilityActivatedEvent(IActivatedAbility ability) 
        : base(EventType.Triggered)
    {
        Ability = ability ?? throw new ArgumentNullException(nameof(ability));
    }
}
