namespace Majik.Core.Abilities;

/// <summary>
/// Interface for static abilities.
/// Static abilities create continuous effects and don't use the stack (Rule 604).
/// </summary>
public interface IStaticAbility
{
    /// <summary>
    /// The source of this static ability (card or permanent).
    /// </summary>
    object Source { get; }

    /// <summary>
    /// The controller of this static ability.
    /// </summary>
    Players.Player Controller { get; }

    /// <summary>
    /// Description of the static ability.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Check if this static ability is currently active.
    /// </summary>
    bool IsActive();

    /// <summary>
    /// Apply the continuous effect of this static ability.
    /// </summary>
    void ApplyEffect();
}
