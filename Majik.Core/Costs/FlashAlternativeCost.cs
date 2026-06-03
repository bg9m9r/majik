using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 601.2b / CR 702.8 — "flash alt-cost permission": an alternative cost
/// that grants its spell a flash casting window in exchange for paying MORE
/// mana. Canonical case: Harbinger of the Tides —
///   "You may cast this spell as though it had flash if you pay {2} more to
///    cast it. (You may cast it any time you could cast an instant.)"
///
/// <para>
/// This is the inverse of the usual "alternative cost = a DISCOUNT" shape
/// (Evoke, Pitch, Flashback). Here the alternative <em>raises</em> the cost —
/// the printed mana cost plus a flat surcharge — and the payoff is a timing
/// permission, not a cheaper cast. The flash window itself is automatic:
/// <see cref="Majik.Core.Game.SpellCastFlow"/> SKIPS the CR 117.1 sorcery-speed
/// gate whenever <i>any</i> <see cref="IAlternativeCost"/> is supplied (the
/// alt-cost is responsible for declaring its own casting permission), so a
/// spell cast for this alternative cost may be cast at instant speed.
/// </para>
///
/// <para>
/// The card is NOT granted flash for its <i>printed</i> cost — declining this
/// alternative cost casts the spell at its normal speed (sorcery speed for a
/// creature, per CR 302.1). The flash window is purchased, per CR 601.2b, only
/// by choosing to pay this alternative cost.
/// </para>
///
/// <para>
/// No resolution side-effect: the surcharge buys nothing but the timing
/// window, so <see cref="OnResolved"/> is a no-op and
/// <see cref="PostResolutionZone"/> follows the printed-type default (a
/// creature still enters the battlefield).
/// </para>
/// </summary>
public sealed class FlashAlternativeCost : IAlternativeCost
{
    /// <summary>
    /// The total mana cost paid for the flash window: the spell's printed
    /// mana cost plus the flat <see cref="SurchargeGeneric"/> surcharge
    /// (Harbinger's "{2} more"). Computed once at construction from the
    /// printed cost so the alternative cost mirrors the card it rides on.
    /// </summary>
    public ManaCost AlternativeManaCost { get; }

    /// <summary>The generic-mana surcharge added on top of the printed cost
    /// (Harbinger of the Tides — {2}).</summary>
    public int SurchargeGeneric { get; }

    public string Description =>
        $"Flash — pay {{{SurchargeGeneric}}} more ({AlternativeManaCost}) to cast at instant speed";

    /// <summary>
    /// Build the flash alternative cost for a card whose printed mana cost is
    /// <paramref name="printedCost"/>, charging <paramref name="surchargeGeneric"/>
    /// extra generic mana on top of it (CR 601.2b — "if you pay {N} more").
    /// </summary>
    public FlashAlternativeCost(ManaCost printedCost, int surchargeGeneric)
    {
        if (printedCost == null) throw new ArgumentNullException(nameof(printedCost));
        if (surchargeGeneric < 0)
            throw new ArgumentOutOfRangeException(nameof(surchargeGeneric),
                "flash surcharge cannot be negative");
        SurchargeGeneric = surchargeGeneric;
        AlternativeManaCost = surchargeGeneric > 0
            ? printedCost.AddGenericCost(surchargeGeneric)
            : printedCost;
    }

    /// <summary>
    /// CR 601.2 — the flash alternative cost is announced at the same step as
    /// the normal cast, so the spell's card must still be in the caster's hand
    /// and owned by the caster. No other restriction: any card carrying this
    /// permission may always opt into the flash window for the surcharge.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        if (card.Zone != ZoneType.Hand) return false;
        return ReferenceEquals(card.Owner, caster);
    }

    /// <summary>No resolution side-effect — the surcharge buys only the timing
    /// window (CR 601.2b), not any zone change or sacrifice.</summary>
    public void OnResolved(ICard card, Player caster)
    {
        // Intentionally empty: the flash permission has no on-resolve rider.
    }
}
