using Majik.Core.Domain.Exceptions;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// Mana cost that must be paid.
/// Uses the ManaCost value object for the cost amount.
/// </summary>
public class ManaCostCost : ICost, ISpendContextCost
{
    private readonly ValueObjects.ManaCost _manaCost;

    public string Description => _manaCost.ToString();
    public ValueObjects.ManaCost Cost => _manaCost;

    public ManaCostCost(ValueObjects.ManaCost manaCost)
    {
        _manaCost = manaCost ?? throw new ArgumentNullException(nameof(manaCost));
    }

    public ManaCostCost(string manaCostString)
    {
        if (string.IsNullOrWhiteSpace(manaCostString))
        {
            _manaCost = ValueObjects.ManaCost.Zero;
        }
        else
        {
            _manaCost = ValueObjects.ManaCost.Parse(manaCostString);
        }
    }

    public virtual bool CanPay(Player player) => CanPay(player, ManaSpendContext.None);

    public virtual void Pay(Player player) => Pay(player, ManaSpendContext.None);

    /// <summary>
    /// CR 106.4 — spend-restriction-aware affordability. Floating restricted
    /// mana the <paramref name="context"/> does NOT permit (Eldrazi Temple's
    /// {C}{C} for a non-Eldrazi ability, Sunken Citadel's double-mana for a
    /// non-land ability / any spell) is treated as UNAVAILABLE for this payment.
    /// The legacy <see cref="CanPay(Player)"/> overload routes here with
    /// <see cref="ManaSpendContext.None"/> — preserving the prior behaviour for
    /// unrestricted mana (the common case) while correctly withholding restricted
    /// mana from a context that doesn't name it.
    /// </summary>
    public virtual bool CanPay(Player player, ManaSpendContext context)
    {
        if (player == null)
        {
            return false;
        }

        return player.CanPayManaUnderSpendContext(_manaCost, context);
    }

    /// <summary>
    /// CR 106.4 — pay this mana cost honouring spend restrictions under
    /// <paramref name="context"/>. Restricted floating mana the context doesn't
    /// permit is withheld across the bucketed spend (it can't pay a non-matching
    /// pip); the satisfying provenance slots are consumed + their reactions
    /// fired. The "or activate abilities of land sources / Eldrazi" half of
    /// Sunken Citadel / Eldrazi Temple is enforced here (the spell-cast half
    /// rides <see cref="ManaPaymentResolver"/>).
    /// </summary>
    public virtual void Pay(Player player, ManaSpendContext context)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (!CanPay(player, context))
        {
            throw new InvalidPlayerActionException($"Cannot pay mana cost: {Description}");
        }

        if (!player.PayManaUnderSpendContext(_manaCost, context))
        {
            throw new InvalidPlayerActionException($"Failed to pay mana cost: {Description}");
        }
    }
}
