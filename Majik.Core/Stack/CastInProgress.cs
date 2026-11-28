using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Stack;

/// <summary>
/// CR 601.2a — a spell whose card has moved to the stack but whose casting is
/// not finished: its caster is still choosing modes, X, targets or paying
/// costs (CR 601.2b–h), and the <see cref="Majik.Core.Spells.Spell"/> object
/// is not pushed yet. Display data only, for state snapshots:
/// <see cref="Stack.GetAll"/>, triggers and rules code never see it.
/// <see cref="Id"/> is the id the spell gets once it is pushed.
/// </summary>
public sealed class CastInProgress
{
    internal CastInProgress(ICard card, Player caster, Guid id)
    {
        Card = card;
        Caster = caster;
        Id = id;
    }

    public ICard Card { get; }
    public Player Caster { get; }
    public Guid Id { get; }

    /// <summary>Target slots chosen so far (CR 601.2c), in announcement order.</summary>
    public IReadOnlyList<IReadOnlyList<object>> Targets { get; internal set; } = Array.Empty<IReadOnlyList<object>>();
}