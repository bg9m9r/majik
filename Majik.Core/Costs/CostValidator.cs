using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// Service for validating costs.
/// </summary>
public class CostValidator
{
    private readonly CostPayment _costPayment;

    public CostValidator()
    {
        _costPayment = new CostPayment();
    }

    /// <summary>
    /// Validate that all costs can be paid.
    /// </summary>
    public bool ValidateCosts(Player player, IEnumerable<ICost> costs)
    {
        if (player == null || costs == null)
        {
            return false;
        }

        return _costPayment.CanPayCosts(player, costs);
    }

    /// <summary>
    /// Get the total mana cost from a list of costs.
    /// </summary>
    public ValueObjects.ManaCost GetTotalManaCost(IEnumerable<ICost> costs)
    {
        if (costs == null)
        {
            return ValueObjects.ManaCost.Zero;
        }

        var totalMana = ValueObjects.ManaCost.Zero;
        foreach (var cost in costs)
        {
            if (cost is ManaCostCost manaCost)
            {
                // TODO: Add mana costs together (need operator+ on ManaCost value object)
                // For now, just return the first mana cost
                return manaCost.Cost;
            }
        }

        return totalMana;
    }
}
