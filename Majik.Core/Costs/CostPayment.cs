using Majik.Core.Domain.Exceptions;
using Majik.Core.Mana;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// Service for paying costs when casting spells or activating abilities.
/// </summary>
public class CostPayment
{
    /// <summary>
    /// Pay all costs in order (no spend context — the legacy path; restricted
    /// floating mana is treated as unavailable, CR 106.4).
    /// </summary>
    public void PayCosts(Player player, IEnumerable<ICost> costs) =>
        PayCosts(player, costs, ManaSpendContext.None);

    /// <summary>
    /// CR 106.4 — pay all costs in order under <paramref name="context"/>. Each
    /// cost that implements <see cref="ISpendContextCost"/> (mana costs) is paid
    /// through the context-aware overload so spend-restricted floating mana
    /// (Sunken Citadel / Eldrazi Temple "abilities of X") is honoured; every
    /// other cost (tap, sacrifice, life) pays through the plain
    /// <see cref="ICost"/> surface. Atomic: all costs are validated before any
    /// is paid.
    /// </summary>
    public void PayCosts(Player player, IEnumerable<ICost> costs, ManaSpendContext context)
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
            var canPay = cost is ISpendContextCost ctxCost
                ? ctxCost.CanPay(player, context)
                : cost.CanPay(player);
            if (!canPay)
            {
                throw new InvalidPlayerActionException($"Cannot pay cost: {cost.Description}");
            }
        }

        // Pay all costs
        foreach (var cost in costList)
        {
            if (cost is ISpendContextCost ctxCost)
            {
                ctxCost.Pay(player, context);
            }
            else
            {
                cost.Pay(player);
            }
        }
    }

    /// <summary>
    /// Check if all costs can be paid (no spend context — legacy path).
    /// </summary>
    public bool CanPayCosts(Player player, IEnumerable<ICost> costs) =>
        CanPayCosts(player, costs, ManaSpendContext.None);

    /// <summary>
    /// CR 106.4 — check if all costs can be paid under <paramref name="context"/>.
    /// Mana costs honour spend restrictions; other costs ignore the context.
    /// </summary>
    public bool CanPayCosts(Player player, IEnumerable<ICost> costs, ManaSpendContext context)
    {
        if (player == null || costs == null)
        {
            return false;
        }

        return costs.All(cost => cost is ISpendContextCost ctxCost
            ? ctxCost.CanPay(player, context)
            : cost.CanPay(player));
    }
}
