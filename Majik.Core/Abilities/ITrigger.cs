namespace Majik.Core.Abilities;

/// <summary>
/// Interface for triggers that cause triggered abilities to fire.
/// </summary>
public interface ITrigger
{
    /// <summary>
    /// Check if the trigger condition is met.
    /// </summary>
    bool IsTriggered();

    /// <summary>
    /// Description of the trigger condition.
    /// </summary>
    string Description { get; }
}
