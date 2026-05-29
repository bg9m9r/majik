using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, discard a card or pay {5}."
/// — Lightning Axe ({R}, Instant). Disjunctive additional cost
/// (CR 601.2f) where the caster picks ONE of the two payment modes at
/// announcement time: discard a card, or pay the generic mana
/// <see cref="ManaCost"/> (printed {5}) on top of the spell's mana cost.
///
/// ## v1 picker policy
/// Sibling shape to <see cref="DiscardACardOrPayLifeAdditionalCost"/>
/// (Bitter Triumph). v1 deterministic preference: <b>discard a card
/// first</b> when one is available — matches the printed wording's
/// first-mode preference and the canonical Lightning Axe play
/// (discarding a dead card / madness enabler is almost always cheaper
/// than spending five mana). When the caster's hand is empty but the
/// mana is producible, the pay-mana mode is used. <see cref="CanPay"/>
/// is the OR of the two modes — payable so long as EITHER mode is.
///
/// After payment exactly one of <see cref="Discarded"/> or
/// <see cref="PaidMana"/> is set, never both.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven mode choice</b>: v1 picks discard-first when both
///   are payable. Full agent prompt ("would you rather discard a card or
///   pay {5}?") shares a queue with <see cref="DiscardACardCost"/>'s
///   deferred discard-target prompt.
/// </summary>
public sealed class DiscardACardOrPayManaAdditionalCost : IAdditionalCost
{
    /// <summary>The mana required by the pay-mana mode (Lightning Axe: {5}).</summary>
    public static readonly ManaCost ManaAmount = ManaCost.Parse("{5}");

    /// <summary>The card discarded by <see cref="Pay"/>, if discard mode
    /// was chosen. Null when pay-mana mode was used or before
    /// payment.</summary>
    public ICard? Discarded { get; private set; }

    /// <summary>True when pay-mana mode was chosen by <see cref="Pay"/>.</summary>
    public bool PaidMana { get; private set; }

    /// <inheritdoc/>
    public string Description => $"discard a card or pay {ManaAmount}";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable if EITHER mode can be paid: at least one card in
    /// the caster's hand (discard mode) OR enough mana in pool to pay
    /// <see cref="ManaAmount"/> (pay-mana mode). Mana legality is checked
    /// against the current pool (CR 601.2f-h — additional costs are paid
    /// after mana abilities are activated; the cast flow gives the caster
    /// the chance to float mana before this check).
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        var hasHandCard = caster.Zones.Hand.GetCards().Any();
        var hasEnoughMana = caster.ManaPool.Pay(ManaAmount).Success;
        return hasHandCard || hasEnoughMana;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// v1 deterministic preference: discard a card when one is available
    /// (CR 601.2f — the caster chooses the mode at announcement; v1
    /// simplifies to a fixed preference). Falls through to the pay-mana
    /// mode when the caster's hand is empty. Discard picker mirrors
    /// <see cref="DiscardACardCost"/> — first card in hand.
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        // Mode 1: discard a card. Same picker as DiscardACardCost — first
        // card in hand.
        var discardPick = caster.Zones.Hand.GetCards().FirstOrDefault();
        if (discardPick != null)
        {
            caster.Zones.Hand.RemoveCard(discardPick);
            caster.Zones.Graveyard.AddCard(discardPick);
            discardPick.SetZone(ZoneType.Graveyard);
            Discarded = discardPick;
            return true;
        }

        // Mode 2: pay {5}.
        if (!caster.PayMana(ManaAmount)) return false;
        PaidMana = true;
        return true;
    }
}
