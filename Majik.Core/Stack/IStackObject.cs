using Majik.Core.Abilities;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

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

    /// <summary>
    /// PLAN 01 — resolve this stack object on the async path (CR 608),
    /// threading the resolver-supplied agent + live game so effects can
    /// await player prompts. Default shim calls the synchronous
    /// <see cref="Resolve"/> so any implementer that has not yet migrated
    /// keeps working. The concrete spell / activated-ability /
    /// triggered-ability classes override this with a real async body.
    /// </summary>
    ValueTask ResolveAsync(
        IPlayerAgent? agent,
        GameContext? game,
        CancellationToken ct = default)
    {
        Resolve();
        return ValueTask.CompletedTask;
    }
}
