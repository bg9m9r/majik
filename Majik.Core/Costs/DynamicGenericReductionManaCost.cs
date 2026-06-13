using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — a <see cref="ManaCostCost"/> whose generic component is reduced
/// at PAYMENT time by a function over the paying player's live game state. The
/// canonical exerciser is the Channel land cycle's "This ability costs {1} less
/// to activate for each legendary creature you control" rider (Boseiju, Who
/// Endures; Otawara; Takenuma; Eiganjo; Sokenzan), but the seam is generic: any
/// activated-ability mana cost that reduces by a board-state count plugs into it.
///
/// <para>
/// The base printed cost is the constructor's <see cref="ValueObjects.ManaCost"/>.
/// At <see cref="CanPay"/> / <see cref="Pay"/> time the reduction function is
/// evaluated against the player paying the cost (the activating controller — the
/// SAME <see cref="Player"/> both <c>CanPay</c> and <c>Pay</c> receive on every
/// cost-payment path: <see cref="CostPayment.CanPayCosts"/>,
/// <see cref="CostPayment.PayCosts"/>, and the live
/// <c>TurnDriver</c>/<c>GameFacade</c> dispatch affordability gate). The reduced
/// cost is produced via <see cref="ValueObjects.ManaCost.WithGeneric"/>, which
/// clamps the new generic to at least the colorless-pip count and never touches
/// colored pips — exactly the CR 118.9 rule that a cost reduction lowers only the
/// generic mana and never below zero (Boseiju's <c>{1}{G}</c> reduced by 2
/// legendary creatures pays <c>{G}</c>, not negative-generic-{G}).
/// </para>
///
/// <para>
/// Because the reduction is computed transparently inside the standard
/// <see cref="ManaCostCost.CanPay"/> / <see cref="ManaCostCost.Pay"/> overrides,
/// NO dispatch-site change is needed: the affordability check, the actual
/// payment, and the bot's colour-blind enumeration all consult the reduced cost
/// automatically. This mirrors how <see cref="VariableXCostExpansion"/> rewrites
/// {X}-bearing costs at activation, but folds the adjustment into the cost object
/// itself rather than rewriting the cost list (the reduction depends on live
/// board state at payment, not on an agent-chosen value, so it has no expansion
/// step to hang off).
/// </para>
/// </summary>
public sealed class DynamicGenericReductionManaCost : ManaCostCost
{
    private readonly Func<Player, int> _genericReduction;

    /// <summary>The printed (un-reduced) base cost, exposed for inspection /
    /// tests / cost-display. The effective cost is
    /// <see cref="EffectiveCost"/>.</summary>
    public ValueObjects.ManaCost BaseCost => Cost;

    /// <param name="baseCost">The printed mana cost before any reduction.</param>
    /// <param name="genericReduction">
    /// Computes how many generic mana to remove, given the player who is paying
    /// (the activating controller). Must be non-null; a negative result is
    /// clamped to 0 (a "reduction" never raises the cost — CR 118.9).
    /// </param>
    public DynamicGenericReductionManaCost(
        ValueObjects.ManaCost baseCost, Func<Player, int> genericReduction)
        : base(baseCost)
    {
        _genericReduction = genericReduction
            ?? throw new ArgumentNullException(nameof(genericReduction));
    }

    /// <param name="baseCostString">Scryfall-style cost text (e.g. "1G").</param>
    /// <param name="genericReduction">See the other constructor.</param>
    public DynamicGenericReductionManaCost(
        string baseCostString, Func<Player, int> genericReduction)
        : base(baseCostString)
    {
        _genericReduction = genericReduction
            ?? throw new ArgumentNullException(nameof(genericReduction));
    }

    /// <summary>The cost actually paid by <paramref name="player"/> after the
    /// CR 118.9 reduction is applied to the base cost's generic component.</summary>
    public ValueObjects.ManaCost EffectiveCost(Player player)
    {
        if (player == null) return Cost;
        var reduction = _genericReduction(player);
        if (reduction <= 0) return Cost;
        return Cost.WithGeneric(Cost.Generic - reduction);
    }

    public override bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.ManaPool.CanPay(EffectiveCost(player));
    }

    public override void Pay(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var effective = EffectiveCost(player);
        if (!player.ManaPool.CanPay(effective))
        {
            throw new Domain.Exceptions.InvalidPlayerActionException(
                $"Cannot pay mana cost: {effective}");
        }
        if (!player.PayMana(effective))
        {
            throw new Domain.Exceptions.InvalidPlayerActionException(
                $"Failed to pay mana cost: {effective}");
        }
    }
}
