using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// "Spells you cast cost {N} less to cast" — applies as a generic-mana
/// reduction during cost calculation. Caller (SpellCastFlow) consults
/// active reductions before prompting the agent for mana.
///
/// MVP: registry exposes active reductions; reduction is min(generic, N)
/// (CR 117.7c — cost can't go below the minimum colored requirements).
/// Reduction is scoped by a predicate that decides whether a given
/// (caster, card) pair qualifies.
/// </summary>
public sealed class CostReductionStaticEffect
{
    public int GenericReduction { get; }
    public Func<Player, ICard, bool> AppliesTo { get; }

    public CostReductionStaticEffect(int genericReduction, Func<Player, ICard, bool> appliesTo)
    {
        if (genericReduction < 0) throw new ArgumentOutOfRangeException(nameof(genericReduction));
        GenericReduction = genericReduction;
        AppliesTo = appliesTo ?? throw new ArgumentNullException(nameof(appliesTo));
    }

    /// <summary>Apply this reduction to <paramref name="cost"/>; returns
    /// the reduced cost. Generic mana floor of 0; colored unchanged.</summary>
    public ManaCost Reduce(ManaCost cost)
    {
        if (cost == null) return cost!;
        var reduced = Math.Max(0, cost.Generic - GenericReduction);
        return cost.WithGeneric(reduced);
    }
}
