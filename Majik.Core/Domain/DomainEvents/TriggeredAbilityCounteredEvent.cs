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
    /// <summary>The reason. Closed set: <see cref="InterveningIfFailed"/>
    /// (the only producer, StackResolver).</summary>
    public string Reason { get; }

    /// <summary>CR 603.4 — the intervening-if condition was false on resolution.</summary>
    public const string InterveningIfFailed = "intervening-if failed at resolution";

    /// <summary>The stack entry id the ability had (its StackObjectDto.Id). For
    /// an ability that triggered more than once (CR 603.3) this names the
    /// firing that was removed, not the ability's own id. Null when unknown.</summary>
    public Guid? EntryId { get; init; }

    public TriggeredAbilityCounteredEvent(ITriggeredAbility ability, string reason)
        : base()
    {
        Ability = ability ?? throw new ArgumentNullException(nameof(ability));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }
}
