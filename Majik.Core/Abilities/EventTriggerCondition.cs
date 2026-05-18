using Majik.Core.Events;

namespace Majik.Core.Abilities;

/// <summary>
/// Trigger condition that fires when an event of type <typeparamref name="TEvent"/>
/// is published and a caller-supplied predicate returns true.
/// </summary>
public sealed class EventTriggerCondition<TEvent> : ITriggerCondition
    where TEvent : GameEvent
{
    private readonly Func<TEvent, ITriggeredAbility, bool> _predicate;

    public Type EventType => typeof(TEvent);

    public EventTriggerCondition(Func<TEvent, ITriggeredAbility, bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool Matches(GameEvent e, ITriggeredAbility ability)
    {
        if (e is not TEvent typed)
        {
            return false;
        }

        return _predicate(typed, ability);
    }
}
