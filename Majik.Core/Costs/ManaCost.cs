using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// Mana cost that must be paid.
/// Uses the ManaCost value object for the cost amount.
/// </summary>
public class ManaCostCost : ICost
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

    public bool CanPay(Player player)
    {
        if (player == null)
        {
            return false;
        }

        return player.ManaPool.CanPay(_manaCost);
    }

    public void Pay(Player player)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (!CanPay(player))
        {
            throw new InvalidPlayerActionException($"Cannot pay mana cost: {Description}");
        }

        if (!player.PayMana(_manaCost))
        {
            throw new InvalidPlayerActionException($"Failed to pay mana cost: {Description}");
        }
    }
}
