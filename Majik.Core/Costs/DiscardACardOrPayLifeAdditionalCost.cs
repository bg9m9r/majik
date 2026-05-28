using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, discard a card or pay 3 life."
/// — Bitter Triumph ({1}{B}, Instant). Disjunctive additional cost
/// (CR 601.2f) where the caster picks ONE of the two payment modes at
/// announcement time.
///
/// ## v1 picker policy
/// Sibling shape to <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/>.
/// v1 deterministic preference: <b>discard a card first</b> when one is
/// available (matches the printed wording's first-mode preference and
/// is the conservative default — paying life risks enabling SBA loss at
/// 1-life scenarios). When the caster's hand is empty but life is
/// sufficient (≥ 3), the pay-life mode is used. <see cref="CanPay"/>
/// is the OR of the two modes — payable so long as EITHER mode is.
///
/// After payment exactly one of <see cref="Discarded"/> or
/// <see cref="PaidLife"/> is set, never both.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven mode choice</b>: v1 picks discard-first when both
///   are payable. Full agent prompt ("would you rather discard a card or
///   pay 3 life?") shares a queue with
///   <see cref="DiscardACardCost"/>'s deferred discard-target prompt.
/// </summary>
public sealed class DiscardACardOrPayLifeAdditionalCost : IAdditionalCost
{
    /// <summary>The amount of life required by the pay-life mode.</summary>
    public const int LifeAmount = 3;

    /// <summary>The card discarded by <see cref="Pay"/>, if discard mode
    /// was chosen. Null when pay-life mode was used or before
    /// payment.</summary>
    public ICard? Discarded { get; private set; }

    /// <summary>True when pay-life mode was chosen by <see cref="Pay"/>.</summary>
    public bool PaidLife { get; private set; }

    /// <inheritdoc/>
    public string Description => $"discard a card or pay {LifeAmount} life";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable if EITHER mode can be paid: at least one card in
    /// the caster's hand (discard mode) OR life total ≥ 3 (pay-life mode).
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        var hasHandCard = caster.Zones.Hand.GetCards().Any();
        var hasEnoughLife = caster.LifeTotal >= LifeAmount;
        return hasHandCard || hasEnoughLife;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// v1 deterministic preference: discard a card when one is available.
    /// Falls through to pay-life mode when the caster's hand is empty
    /// (CR 601.2f — the caster chooses the mode at announcement; v1
    /// simplifies to a fixed preference).
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

        // Mode 2: pay 3 life.
        if (caster.LifeTotal < LifeAmount) return false;

        caster.LoseLife(LifeAmount);
        PaidLife = true;
        return true;
    }
}
