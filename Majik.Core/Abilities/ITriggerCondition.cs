using Majik.Core.Events;

namespace Majik.Core.Abilities;

/// <summary>
/// Predicate that decides whether a game event causes a triggered ability to fire.
/// </summary>
public interface ITriggerCondition
{
    /// <summary>
    /// Concrete event type this condition cares about. Used by TriggerManager to
    /// subscribe selectively and short-circuit non-matching events.
    /// </summary>
    Type EventType { get; }

    /// <summary>
    /// True iff the event causes the ability to fire.
    /// </summary>
    bool Matches(GameEvent e, ITriggeredAbility ability);
}
