using Majik.Core.Events;

namespace Majik.Core.Abilities;

/// <summary>
/// Rule 603.2c: a state-change trigger condition. Unlike event triggers, these
/// observe a boolean predicate over the game state and fire on the
/// false→true transition only. Stays "armed" while true; re-armed when the
/// predicate returns to false.
///
/// Evaluated by <see cref="TriggerManager.EvaluateStateChangeTriggers"/>, which
/// is invoked by <see cref="Majik.Core.Rules.StateBasedActions"/> after each
/// SBA pass.
/// </summary>
public sealed class StateChangeTriggerCondition : ITriggerCondition
{
    private readonly Func<bool> _predicate;
    private bool _lastWasTrue;

    /// <summary>
    /// Sentinel — state-change triggers do not participate in event dispatch.
    /// </summary>
    public Type EventType => typeof(StateChangeTriggerCondition);

    public StateChangeTriggerCondition(Func<bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool Matches(GameEvent e, ITriggeredAbility ability) => false;

    /// <summary>
    /// Returns true on the rising edge of the underlying predicate.
    /// </summary>
    public bool IsSatisfied()
    {
        var now = _predicate();
        if (now && !_lastWasTrue)
        {
            _lastWasTrue = true;
            return true;
        }

        if (!now)
        {
            _lastWasTrue = false;
        }

        return false;
    }
}
