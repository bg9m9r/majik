using Majik.Core.Abilities;
using Majik.Core.Events;

namespace Majik.Core.Domain.DomainEvents;

/// <summary>
/// Rule 603.4: when a triggered ability with an intervening-if clause reaches
/// resolution and the clause is false, the ability is removed from the stack
/// and its effects do not happen ("countered").
/// </summary>
public class TriggeredAbilityCounteredEvent : GameEvent
{
    public ITriggeredAbility Ability { get; }
    public string Reason { get; }

    public TriggeredAbilityCounteredEvent(ITriggeredAbility ability, string reason)
        : base(EventType.Triggered)
    {
        Ability = ability ?? throw new ArgumentNullException(nameof(ability));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }
}
