using Majik.Core.Costs;
using Majik.Core.Stack;
using Majik.Core.Targeting;

namespace Majik.Core.Abilities;

/// <summary>
/// Interface for activated abilities on the stack.
/// </summary>
public interface IActivatedAbility : IStackObject
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
    /// The costs paid for this ability.
    /// </summary>
    IReadOnlyList<ICost> Costs { get; }
}
