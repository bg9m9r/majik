using Majik.Core.Cards;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// Additional costs beyond mana (sacrifice, tap, pay life).
/// Discard-as-cost lives in <see cref="DiscardXCardsAdditionalCost"/>.
/// </summary>
public class AdditionalCost : ICost, IBusAwareCost
{
    private readonly AdditionalCostType _costType;
    private readonly object? _costParameter;
    private readonly IEventBus? _eventBus;

    public string Description { get; }
    public AdditionalCostType CostType => _costType;

    /// <summary>
    /// The permanent this cost taps or sacrifices, when the parameter is a
    /// permanent (CR 602.2 cost analysis). Null for non-permanent costs (life,
    /// counters). Lets the priority-kinds gate tell a "{T}" self-tap ability
    /// (blocked by summoning sickness) apart from a non-tap activated ability
    /// (Yawgmoth's "Pay 1 life, Sacrifice another creature", which a sick
    /// creature CAN still activate).
    /// </summary>
    public Cards.Permanent? Permanent => _costParameter as Cards.Permanent;

    private AdditionalCost(AdditionalCostType costType, string description, object? costParameter = null, IEventBus? eventBus = null)
    {
        _costType = costType;
        Description = description;
        _costParameter = costParameter;
        _eventBus = eventBus;
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
    /// <param name="permanent">The permanent sacrificed as the cost.</param>
    /// <param name="eventBus">Optional event bus. When supplied, paying the
    /// cost publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a)
    /// crediting the cost-payer (the permanent's controller) as the
    /// sacrificing player, so "whenever an opponent sacrifices …" aristocrat
    /// payoffs fire on sac-cost activation paths. Null preserves the legacy
    /// publish-nothing posture.</param>
    public static AdditionalCost Sacrifice(Cards.Permanent permanent, IEventBus? eventBus = null)
    {
        if (permanent == null)
        {
            throw new ArgumentNullException(nameof(permanent));
        }

        return new AdditionalCost(AdditionalCostType.Sacrifice, $"Sacrifice {permanent.Name}", permanent, eventBus);
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

    /// <summary>
    /// STAGE 1 (re-sourceable abilities) — return a NEW <see cref="AdditionalCost"/>
    /// identical to this one EXCEPT that, for <see cref="AdditionalCostType.Tap"/>
    /// / <see cref="AdditionalCostType.Sacrifice"/> costs whose captured permanent
    /// (<see cref="_costParameter"/>) is reference-equal to <paramref name="oldSource"/>,
    /// the captured permanent is swapped to <paramref name="newSource"/>.
    ///
    /// <para>
    /// This re-homes the self-referential <c>{T}</c> / sacrifice cost of an
    /// activated ability when the ability is re-sourced onto a new permanent
    /// (CR 707.2 copy machinery / Agatha's Soul Cauldron granted abilities), so
    /// the rebound ability taps / sacrifices the NEW source rather than the
    /// permanent the original cost captured. Mana / pay-life costs do not
    /// reference a source permanent, and costs whose captured permanent does not
    /// match <paramref name="oldSource"/> are returned unchanged (this instance).
    /// The description is rebuilt to name the new permanent; the event bus
    /// (sacrifice-cost aristocrat publishing) is preserved.
    /// </para>
    /// </summary>
    public AdditionalCost RebindSource(object oldSource, object newSource)
    {
        // Only the source-capturing cost types reference a permanent; mana /
        // pay-life carry an int or nothing, so there is nothing to rebind.
        if (_costType is not (AdditionalCostType.Tap or AdditionalCostType.Sacrifice))
        {
            return this;
        }

        // Swap only when this cost's captured permanent IS the old source
        // (reference equality — CR 707.2 re-home of the ability's own {T}/sac).
        if (!ReferenceEquals(_costParameter, oldSource)
            || newSource is not Cards.Permanent newPermanent)
        {
            return this;
        }

        var description = _costType == AdditionalCostType.Tap
            ? $"Tap {newPermanent.Name}"
            : $"Sacrifice {newPermanent.Name}";

        return new AdditionalCost(_costType, description, newPermanent, _eventBus);
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

    /// <inheritdoc/>
    /// <remarks>The legacy / bus-less drive path. Honours a bus threaded at
    /// construction (<see cref="_eventBus"/>) so a factory that explicitly
    /// wired its own bus keeps publishing on the direct <see cref="ICost.Pay(Player)"/>
    /// path; null construction bus = publish-nothing posture.</remarks>
    public void Pay(Player player) => PayCore(player, _eventBus);

    /// <summary>
    /// CR 701.16a — central-seam payment. <see cref="Costs.CostPayment.PayCosts(Player, System.Collections.Generic.IEnumerable{ICost}, Mana.ManaSpendContext, IEventBus)"/>
    /// routes any cost implementing <see cref="IBusAwareCost"/> here when a bus
    /// is supplied, so a "Sacrifice CARDNAME:" activated-ability cost publishes
    /// a <see cref="PermanentSacrificedEvent"/> on the central cost-payment path
    /// WITHOUT each factory needing a bespoke bus-bearing Create overload (the
    /// per-factory Festival-Crasher thread is now obsolete for the class-(b)
    /// sac-cost tail). A bus threaded at construction (<see cref="_eventBus"/>)
    /// takes precedence and publishes exactly once — so a factory that already
    /// wired its own bus keeps identical behaviour and never double-fires; only
    /// when no construction bus was set does the seam bus carry the publish.
    /// State effects are identical to <see cref="Pay(Player)"/>; the bus only
    /// adds the observable event.
    /// </summary>
    public void Pay(Player player, IEventBus eventBus)
    {
        if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
        // Construction bus wins (back-compat); otherwise use the seam bus.
        PayCore(player, _eventBus ?? eventBus);
    }

    private void PayCore(Player player, IEventBus? eventBus)
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

                    // CR 111.7 — snapshot token-ness BEFORE the move; a token
                    // ceases to exist as an SBA the instant it reaches the
                    // graveyard, so the flag must be read while it is still the
                    // live battlefield object.
                    var wasToken = sac.IsToken;

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

                    // CR 701.16a — the cost-payer (the permanent's controller)
                    // is the sacrificing player. Publish AFTER the move so a
                    // payoff that reads the sacrificed card finds it in the
                    // graveyard (mirrors Fx.Sacrifice's bus-aware overload).
                    eventBus?.Publish(new PermanentSacrificedEvent(sac, player, wasToken));
                }
                break;

            case AdditionalCostType.PayLife:
                if (_costParameter is int amount)
                {
                    player.LoseLife(amount);

                    // CR 118.8 / CR 119.4 — paying life as a cost publishes a
                    // LifePaidEvent (life PAYMENT provenance, distinct from a
                    // LifeChangedEvent decrease) so a "whenever a player pays
                    // life …" payoff fires. Paying 0 life is not "paying life"
                    // (CR 119.4) — suppress the publish. The bus is the central
                    // seam bus / the construction bus, exactly like the
                    // sacrifice publish above.
                    if (amount > 0)
                    {
                        eventBus?.Publish(new LifePaidEvent(player, amount, wasCost: true));
                    }
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
