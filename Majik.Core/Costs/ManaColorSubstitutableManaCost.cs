using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// A mana <see cref="ICost"/> that honours an active payment-time
/// mana-colour-substitution permission (CR 609.4b) for a given
/// <see cref="ManaSpendPurpose"/>.
///
/// When the paying player has an active
/// <see cref="ManaColorSubstitutionPermission"/> for <see cref="_purpose"/>
/// (e.g. Agatha's Soul Cauldron's "you may spend mana as though it were mana of
/// any color to activate abilities of creatures you control"), the coloured
/// pips of the underlying cost are folded into generic
/// (<see cref="ValueObjects.ManaCost.WithColoredFoldedToGeneric"/>) so any mana
/// of any colour qualifies. The mana value is unchanged (CR 106.6); only which
/// mana satisfies a coloured pip widens.
///
/// This is the reusable consumer of the
/// <see cref="ManaColorSubstitutionPermission"/> primitive — the same
/// permissive fold the
/// <see cref="ManaPaymentResolver.Pay(Player, ValueObjects.ManaCost, Players.Agents.ManaPayment, bool)"/>
/// <c>spendAsAnyColor</c> seam applies for Robber of the Rich, but driven by a
/// static permission rather than a per-card runtime grant. Use it as the mana
/// component of an activated ability whose source widens its controller's
/// colour requirements.
/// </summary>
public sealed class ManaColorSubstitutableManaCost : ManaCostCost
{
    private readonly ValueObjects.ManaCost _printedCost;
    private readonly Player _controller;
    private readonly ManaSpendPurpose _purpose;

    public ManaColorSubstitutableManaCost(
        ValueObjects.ManaCost cost, Player controller, ManaSpendPurpose purpose)
        : base(cost)
    {
        _printedCost = cost ?? throw new ArgumentNullException(nameof(cost));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _purpose = purpose;
    }

    /// <summary>
    /// The cost actually matched against the pool: the printed cost when no
    /// substitution permission is active, or the coloured-pips-folded cost when
    /// one is (CR 609.4b). Re-evaluated each call so a permission gained / lost
    /// between announcement and payment is honoured.
    /// </summary>
    private ValueObjects.ManaCost EffectiveCost() =>
        ManaColorSubstitutionPermission.PlayerMaySpendAnyColorFor(_controller, _purpose)
            ? _printedCost.WithColoredFoldedToGeneric()
            : _printedCost;

    public override bool CanPay(Player player)
    {
        if (player == null)
        {
            return false;
        }

        return player.ManaPool.CanPay(EffectiveCost());
    }

    public override void Pay(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var effective = EffectiveCost();
        if (!player.ManaPool.CanPay(effective))
        {
            throw new Domain.Exceptions.InvalidPlayerActionException(
                $"Cannot pay mana cost: {Description}");
        }

        if (!player.PayMana(effective))
        {
            throw new Domain.Exceptions.InvalidPlayerActionException(
                $"Failed to pay mana cost: {Description}");
        }
    }
}
