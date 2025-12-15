using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Abilities;

/// <summary>
/// Service for managing triggered abilities.
/// Evaluates triggers on events and automatically places triggered abilities on the stack (Rule 603).
/// </summary>
public class TriggerManager
{
    private readonly List<ITriggeredAbility> _triggeredAbilities = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly IEventBus? _eventBus;

    public TriggerManager(Majik.Core.Stack.Stack stack, IEventBus? eventBus = null)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _eventBus = eventBus;
    }

    /// <summary>
    /// Register a triggered ability to be evaluated.
    /// </summary>
    public void RegisterTriggeredAbility(ITriggeredAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        if (!_triggeredAbilities.Contains(ability))
        {
            _triggeredAbilities.Add(ability);
        }
    }

    /// <summary>
    /// Unregister a triggered ability.
    /// </summary>
    public void UnregisterTriggeredAbility(ITriggeredAbility ability)
    {
        if (ability == null)
        {
            return;
        }

        _triggeredAbilities.Remove(ability);
    }

    /// <summary>
    /// Evaluate triggers for a game event.
    /// This should be called whenever an event occurs that might trigger abilities.
    /// </summary>
    public void EvaluateTriggers(GameEvent gameEvent)
    {
        if (gameEvent == null)
        {
            return;
        }

        var triggered = new List<ITriggeredAbility>();

        // Check each registered ability to see if it triggers
        foreach (var ability in _triggeredAbilities.ToList())
        {
            if (ability.IsTriggered() && ability.CanBePutOnStack())
            {
                triggered.Add(ability);
            }
        }

        // Put triggered abilities on the stack (Rule 603.2)
        foreach (var ability in triggered)
        {
            _stack.Push(ability);
            _eventBus?.Publish(new TriggeredAbilityTriggeredEvent(ability, gameEvent));
        }
    }

    /// <summary>
    /// Clear all registered triggered abilities.
    /// </summary>
    public void Clear()
    {
        _triggeredAbilities.Clear();
    }
}
