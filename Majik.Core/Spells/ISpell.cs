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

    /// <summary>
    /// CR 701.5b — "an uncounterable spell can't be countered". When this
    /// flag is <c>true</c> the spell ignores attempts to counter it via
    /// <see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>.
    /// Stamped at cast time by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> based on a
    /// <c>KeywordAbility("Uncounterable")</c> marker on the spell's card
    /// (Emrakul, the Aeons Torn / Apocalypse Hydra / et al.); future
    /// sources of cast-time uncounterability (Vexing Shusher trigger,
    /// per-cast stamps) reuse the same surface.
    ///
    /// Defaults to <c>false</c> so hand-built test spells without an
    /// explicit stamp behave as normal (counterable) casts.
    /// </summary>
    bool CannotBeCountered { get; }
}
