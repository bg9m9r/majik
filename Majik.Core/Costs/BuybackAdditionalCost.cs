using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.27 — Buyback. Optional additional cost. If paid, the spell
/// returns to its owner's hand instead of going to the graveyard as it
/// resolves (CR 702.27c). Implemented as an <see cref="IAdditionalCost"/>
/// that pays mana on activation + queues a return-to-hand on resolve.
///
/// To use: caller adds a buyback cost via SpellCastFlow.additionalCosts,
/// then chains a "return to hand" cleanup as a final effect on the spell
/// (using a small wrapper effect that calls <see cref="ReturnOnResolve"/>).
/// </summary>
public sealed class BuybackAdditionalCost : IAdditionalCost
{
    private readonly ICard _card;
    private readonly ManaCost _cost;

    public BuybackAdditionalCost(ICard card, ManaCost cost)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _cost = cost ?? throw new ArgumentNullException(nameof(cost));
    }

    public string Description => $"Buyback {_cost}";

    public bool CanPay(Player caster) =>
        caster.ManaPool.Pay(_cost).Success;

    public bool Pay(Player caster) => caster.PayMana(_cost);

    /// <summary>Cleanup effect to chain onto the spell: returns card from
    /// stack to caster's hand on resolution.</summary>
    public void ReturnOnResolve(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (_card.Zone != ZoneType.Stack) return;
        caster.Zones.Hand.AddCard(_card);
        _card.Zone = ZoneType.Hand;
    }
}
