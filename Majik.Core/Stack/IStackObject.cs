using Majik.Core.Players;

namespace Majik.Core.Stack;

/// <summary>
/// Interface for objects that can be placed on the stack.
/// Includes spells, activated abilities, and triggered abilities.
/// </summary>
public interface IStackObject
{
    /// <summary>
    /// Unique identifier for this stack object.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The player who controls this stack object (who cast/activated it).
    /// </summary>
    Player Controller { get; }

    /// <summary>
    /// Timestamp when this object was added to the stack.
    /// </summary>
    DateTime Timestamp { get; }

    /// <summary>
    /// Whether this object is currently resolving.
    /// </summary>
    bool IsResolving { get; }

    /// <summary>
    /// Resolve this stack object.
    /// </summary>
    void Resolve();
}
