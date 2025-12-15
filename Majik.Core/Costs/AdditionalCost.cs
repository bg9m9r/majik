using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// Additional costs beyond mana (sacrifice, tap, discard, etc.).
/// </summary>
public class AdditionalCost : ICost
{
    private readonly AdditionalCostType _costType;
    private readonly object? _costParameter;

    public string Description { get; }
    public AdditionalCostType CostType => _costType;

    private AdditionalCost(AdditionalCostType costType, string description, object? costParameter = null)
    {
        _costType = costType;
        Description = description;
        _costParameter = costParameter;
    }

    /// <summary>
    /// Create a tap cost (tap a permanent).
    /// </summary>
    public static AdditionalCost Tap(Cards.Permanent permanent)
    {
        if (permanent == null)
        {
            throw new ArgumentNullException(nameof(permanent));
        }

        return new AdditionalCost(AdditionalCostType.Tap, $"Tap {permanent.Name}", permanent);
    }

    /// <summary>
    /// Create a sacrifice cost (sacrifice a permanent).
    /// </summary>
    public static AdditionalCost Sacrifice(Cards.Permanent permanent)
    {
        if (permanent == null)
        {
            throw new ArgumentNullException(nameof(permanent));
        }

        return new AdditionalCost(AdditionalCostType.Sacrifice, $"Sacrifice {permanent.Name}", permanent);
    }

    /// <summary>
    /// Create a discard cost (discard a card).
    /// </summary>
    public static AdditionalCost Discard(ICard card)
    {
        if (card == null)
        {
            throw new ArgumentNullException(nameof(card));
        }

        return new AdditionalCost(AdditionalCostType.Discard, $"Discard {card.Name}", card);
    }

    /// <summary>
    /// Create a life cost (pay life).
    /// </summary>
    public static AdditionalCost PayLife(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Life amount cannot be negative", nameof(amount));
        }

        return new AdditionalCost(AdditionalCostType.PayLife, $"Pay {amount} life", amount);
    }

    public bool CanPay(Player player)
    {
        if (player == null)
        {
            return false;
        }

        return _costType switch
        {
            AdditionalCostType.Tap => _costParameter is Cards.Permanent permanent && !permanent.IsTapped,
            AdditionalCostType.Sacrifice => _costParameter is Cards.Permanent permanent && permanent.Controller == player,
            AdditionalCostType.Discard => _costParameter is ICard card && card.Controller == player && card.Zone == ZoneType.Hand,
            AdditionalCostType.PayLife => _costParameter is int amount && player.LifeTotal > amount,
            _ => false
        };
    }

    public void Pay(Player player)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (!CanPay(player))
        {
            throw new InvalidPlayerActionException($"Cannot pay additional cost: {Description}");
        }

        switch (_costType)
        {
            case AdditionalCostType.Tap:
                if (_costParameter is Cards.Permanent permanent)
                {
                    permanent.Tap();
                }
                break;

            case AdditionalCostType.Sacrifice:
                // TODO: Move permanent to graveyard (zone service)
                break;

            case AdditionalCostType.Discard:
                // TODO: Move card to graveyard (zone service)
                break;

            case AdditionalCostType.PayLife:
                if (_costParameter is int amount)
                {
                    player.LoseLife(amount);
                }
                break;
        }
    }
}

/// <summary>
/// Types of additional costs.
/// </summary>
public enum AdditionalCostType
{
    Tap,
    Sacrifice,
    Discard,
    PayLife
}
