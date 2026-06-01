using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// Additional costs beyond mana (sacrifice, tap, pay life).
/// Discard-as-cost lives in <see cref="DiscardXCardsAdditionalCost"/>.
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
            // CR 302.6 / 605.3a — the {T} tap cost is the choke point every
            // {T} activated ability's cost payment passes through. Beyond the
            // permanent being untapped, a creature paying {T} must not be
            // summoning sick (unless it has haste — CR 702.10). The central
            // gate is creature-only, so land / artifact tap costs are
            // unaffected. AdditionalCost.Tap(...) always taps the ability's
            // own source, so gating the tapped permanent enforces CR 302.6 on
            // the right creature.
            AdditionalCostType.Tap => _costParameter is Cards.Permanent permanent
                && !permanent.IsTapped
                && Abilities.SummoningSicknessTapGate.CanTapForAbility(permanent),
            AdditionalCostType.Sacrifice => _costParameter is Cards.Permanent permanent && permanent.Controller == player,
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
                // CR 701.16 — move the permanent from its controller's
                // battlefield to its owner's graveyard. Route through
                // ZoneService when a per-player service is registered so
                // CardMovedEvent fires (sac triggers — Sakura-Tribe Elder,
                // Bloodghast, Bridge from Below, Korlash, dredge, etc. all
                // depend on it) and replacement effects (LTBs) run. Falls
                // back to raw zone manipulation when no service is
                // registered (unit-test shape with no live game).
                if (_costParameter is Cards.Permanent sac)
                {
                    var ownerOfSac = sac.Owner;
                    if (ownerOfSac == null) break;
                    var holder = sac.Controller ?? ownerOfSac;
                    if (sac.Zone != ZoneType.Battlefield) break;

                    var zones = ZoneServiceRegistry.Get(holder);
                    if (zones != null)
                    {
                        zones.MoveCard(sac, ZoneType.Battlefield, ZoneType.Graveyard, ownerOfSac);
                    }
                    else
                    {
                        holder.Zones.Battlefield.RemoveCard(sac);
                        ownerOfSac.Zones.Graveyard.AddCard(sac);
                        sac.SetZone(ZoneType.Graveyard);
                    }
                }
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
    PayLife
}
