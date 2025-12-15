using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// Service for paying costs when casting spells or activating abilities.
/// </summary>
public class CostPayment
{
    /// <summary>
    /// Pay all costs in order.
    /// </summary>
    public void PayCosts(Player player, IEnumerable<ICost> costs)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (costs == null)
        {
            throw new ArgumentNullException(nameof(costs));
        }

        var costList = costs.ToList();

        // Validate all costs can be paid before paying any
        foreach (var cost in costList)
        {
            if (!cost.CanPay(player))
            {
                throw new InvalidPlayerActionException($"Cannot pay cost: {cost.Description}");
            }
        }

        // Pay all costs
        foreach (var cost in costList)
        {
            cost.Pay(player);
        }
    }

    /// <summary>
    /// Check if all costs can be paid.
    /// </summary>
    public bool CanPayCosts(Player player, IEnumerable<ICost> costs)
    {
        if (player == null || costs == null)
        {
            return false;
        }

        return costs.All(cost => cost.CanPay(player));
    }
}
