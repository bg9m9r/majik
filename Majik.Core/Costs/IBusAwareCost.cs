using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// A cost whose payment produces game events (CR 701.16 — a sacrifice as a
/// cost is still a sacrifice that "whenever a/an [player] sacrifices …"
/// aristocrat triggers must observe). The plain <see cref="ICost.Pay(Player)"/>
/// signature carries no <see cref="IEventBus"/>, so a self-sacrifice cost paid
/// through it (a "Sacrifice CARDNAME:" activated-ability cost) had no central
/// seam to publish a <see cref="PermanentSacrificedEvent"/> on — only the
/// effect-side <see cref="Primitives.Fx.Sacrifice(ICard, Player, IEventBus)"/>
/// overload could. This marker adds that seam.
///
/// <para><see cref="Costs.CostPayment.PayCosts(Player, System.Collections.Generic.IEnumerable{ICost}, Mana.ManaSpendContext, IEventBus)"/>
/// routes any cost implementing this through <see cref="Pay(Player, IEventBus)"/>
/// when a bus is supplied, so cost-payment publishes on the SAME central
/// cost-payment path that pays mana / tap / life — exactly mirroring how
/// <see cref="ISpendContextCost"/> lets mana costs read a
/// <see cref="Mana.ManaSpendContext"/> at the spend site. A bus-aware cost MUST
/// still implement the plain <see cref="ICost.Pay(Player)"/> (the bus-less
/// legacy path) with identical state effects minus the publish.</para>
/// </summary>
public interface IBusAwareCost
{
    /// <summary>
    /// CR 701.16 — pay the cost AND publish any resulting events on
    /// <paramref name="eventBus"/>. State effects must match the plain
    /// <see cref="ICost.Pay(Player)"/> exactly; the bus only adds the
    /// observable event(s).
    /// </summary>
    void Pay(Player player, IEventBus eventBus);
}
