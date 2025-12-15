using Majik.Core.Stack;
using Majik.Core.Targeting;

namespace Majik.Core.Abilities;

/// <summary>
/// Interface for triggered abilities.
/// Triggered abilities fire automatically when their trigger condition is met (Rule 603).
/// </summary>
public interface ITriggeredAbility : IStackObject
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
    /// Check if the trigger condition is met.
    /// </summary>
    bool IsTriggered();

    /// <summary>
    /// Check if the ability can be put on the stack.
    /// </summary>
    bool CanBePutOnStack();
}
