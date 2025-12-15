using Majik.Core.Events;

namespace Majik.Core.Abilities;

/// <summary>
/// Interface for replacement effects.
/// Replacement effects modify events before they occur (Rule 614).
/// </summary>
public interface IReplacementEffect
{
    /// <summary>
    /// The source of this replacement effect (card or permanent).
    /// </summary>
    object Source { get; }

    /// <summary>
    /// The controller of this replacement effect.
    /// </summary>
    Players.Player Controller { get; }

    /// <summary>
    /// Description of the replacement effect.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Check if this replacement effect can replace the given event.
    /// </summary>
    bool CanReplace(GameEvent gameEvent);

    /// <summary>
    /// Replace the event with a modified version.
    /// Returns the modified event, or null if the event should be prevented.
    /// </summary>
    GameEvent? Replace(GameEvent gameEvent);
}
