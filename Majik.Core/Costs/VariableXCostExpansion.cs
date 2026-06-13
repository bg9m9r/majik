using Majik.Core.Abilities;

namespace Majik.Core.Costs;

/// <summary>
/// GAP 2 — shared helper for expanding the {X} in a variable-X ACTIVATED
/// ability's mana cost to a concrete generic amount, reusing the SAME machinery
/// the spell path uses (<see cref="ValueObjects.ManaCost.AddGenericCost"/>;
/// cf. <c>SpellCastFlow.ComputeAndApplyTotalCost</c>'s
/// <c>totalCost.AddGenericCost(xValue)</c> fold).
///
/// <para>
/// An activated ability stores its mana cost as a <see cref="ManaCostCost"/>
/// (one of the entries in <see cref="ActivatedAbility.Costs"/>) whose
/// <see cref="ValueObjects.ManaCost.HasX"/> flag is the variable-X predicate —
/// identical to <c>SpellDefinition.HasVariableX</c> on the spell side. When that
/// flag is set, the printed {X} has <c>Generic == 0</c> on the production routed
/// build, so paying it consumes nothing and the ability resolves with X = 0.
/// This helper rebuilds the cost list with each {X}-bearing
/// <see cref="ManaCostCost"/> replaced by one whose generic component has been
/// raised by the chosen X, so <see cref="CostPayment.PayCosts"/> drains the real
/// {base + X} amount. Non-mana costs and fixed mana costs pass through
/// unchanged.
/// </para>
/// </summary>
public static class VariableXCostExpansion
{
    /// <summary>
    /// True iff any cost in <paramref name="costs"/> is a <see cref="ManaCostCost"/>
    /// whose mana cost contains {X} (CR 107.3 — the variable-X predicate, the
    /// same <see cref="ValueObjects.ManaCost.HasX"/> the spell path reads).
    /// </summary>
    public static bool HasVariableX(IEnumerable<ICost> costs)
    {
        foreach (var c in costs)
        {
            if (c is ManaCostCost mc && mc.Cost.HasX)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Return a cost list with every {X}-bearing <see cref="ManaCostCost"/>
    /// expanded so its generic component is raised by <paramref name="x"/> (the
    /// chosen X), via <see cref="ValueObjects.ManaCost.AddGenericCost"/>. All
    /// other costs (fixed mana, sacrifice, tap, return-a-land, …) pass through
    /// by reference. When no cost carries {X} the original list is returned
    /// unchanged. <paramref name="x"/> is clamped to a minimum of 0.
    /// </summary>
    public static IReadOnlyList<ICost> Expand(IReadOnlyList<ICost> costs, int x)
    {
        if (x < 0) x = 0;
        if (!HasVariableX(costs)) return costs;

        var expanded = new List<ICost>(costs.Count);
        foreach (var c in costs)
        {
            if (c is ManaCostCost mc && mc.Cost.HasX)
            {
                expanded.Add(new ManaCostCost(mc.Cost.AddGenericCost(x)));
            }
            else
            {
                expanded.Add(c);
            }
        }
        return expanded;
    }
}
