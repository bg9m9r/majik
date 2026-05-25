using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Pay N life." Activated-ability life payment cost (CR 118.8 / 119.4).
/// Sibling of <see cref="PayLifeAdditionalCost"/>: <see cref="ICost"/> shape
/// so it slots directly into <see cref="Majik.Core.Abilities.ActivatedAbility"/>
/// cost lists alongside <see cref="ManaCostCost"/> and
/// <see cref="DiscardSelfCost"/>.
///
/// Used by Street Wraith's cycling alt-cost ("Pay 2 life, Discard this
/// card: Draw a card"), Phyrexian Tower, Greed, Necropotence, and any
/// other activated ability with an explicit pay-N-life rider.
///
/// CR 119.4 — you can't pay life you don't have. <see cref="CanPay"/>
/// gates on <c>LifeTotal &gt;= amount</c>; activation fails before the
/// ability hits the stack when the payer is short on life.
/// </summary>
public sealed class PayLifeCost : ICost
{
    private readonly int _amount;

    public PayLifeCost(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount),
                "Pay-life amount must be non-negative.");
        _amount = amount;
    }

    /// <summary>The amount of life that will be paid on <see cref="Pay"/>.</summary>
    public int Amount => _amount;

    /// <inheritdoc/>
    public string Description => $"pay {_amount} life";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 119.4 — paying N life requires <c>LifeTotal &gt;= N</c>. Paying
    /// 0 life is always legal (no-op).
    /// </remarks>
    public bool CanPay(Player player) =>
        player != null && player.LifeTotal >= _amount;

    /// <inheritdoc/>
    /// <remarks>
    /// Routes through <see cref="Player.LoseLife"/> so any life-loss
    /// replacement / triggers (e.g. Sanguine Bond / Vizkopa Guildmage)
    /// fire as expected. Throws if preconditions aren't met (mirrors
    /// <see cref="ManaCostCost"/> + <see cref="DiscardSelfCost"/>).
    /// </remarks>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (!CanPay(player))
            throw new InvalidPlayerActionException(
                $"Cannot {Description}: {player.Name} has {player.LifeTotal} life.");
        if (_amount > 0) player.LoseLife(_amount);
    }
}
