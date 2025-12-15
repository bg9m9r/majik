using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Stack;
using Majik.Core.Targeting;

namespace Majik.Core.Spells;

/// <summary>
/// Interface for spells on the stack.
/// </summary>
public interface ISpell : IStackObject
{
    /// <summary>
    /// The card that represents this spell.
    /// </summary>
    ICard Card { get; }

    /// <summary>
    /// The targets chosen for this spell.
    /// </summary>
    IReadOnlyList<ITarget> Targets { get; }

    /// <summary>
    /// The costs paid for this spell.
    /// </summary>
    IReadOnlyList<ICost> Costs { get; }
}
