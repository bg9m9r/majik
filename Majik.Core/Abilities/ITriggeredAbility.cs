using Majik.Core.Events;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// Interface for triggered abilities.
/// Triggered abilities fire automatically when their trigger condition is met (Rule 603).
/// </summary>
public interface ITriggeredAbility : IStackObject, IAbility
{
    /// <summary>
    /// The source of this ability (card or permanent).
    /// </summary>
    object Source { get; }

    /// <summary>
    /// The targets chosen for this ability (if any).
    /// </summary>
    IReadOnlyList<ITarget> Targets { get; }

    /// <summary>
    /// Condition that decides whether a published event fires this ability.
    /// </summary>
    ITriggerCondition Condition { get; }

    /// <summary>
    /// Optional intervening-if predicate (Rule 603.4). Checked when the trigger
    /// would be put on the stack AND again on resolution.
    /// </summary>
    Func<bool>? InterveningIf { get; }

    /// <summary>
    /// Zones in which the source must reside for the ability to function (Rule 603.6a).
    /// Defaults to <see cref="ZoneType.Battlefield"/>.
    /// </summary>
    IReadOnlySet<ZoneType> ActiveZones { get; }

    /// <summary>
    /// Check if the trigger condition is met for the given event.
    /// </summary>
    bool IsTriggered(GameEvent e);

    /// <summary>
    /// Check if the ability can be put on the stack (intervening-if check).
    /// </summary>
    bool CanBePutOnStack();
}
