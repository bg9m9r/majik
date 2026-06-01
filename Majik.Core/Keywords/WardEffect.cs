using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.21 — Ward {cost}: "Whenever this permanent becomes the target of a
/// spell or ability an opponent controls, counter that spell or ability unless
/// its controller pays {cost}."
///
/// Ward technically TRIGGERS (CR 702.21e — a triggered ability), then on
/// resolution offers the targeting player the choice to pay {cost}; if they
/// don't, the spell or ability is countered (CR 702.21f). This primitive
/// exposes that resolution as a check helper that the spell-resolution path
/// invokes when an opponent's spell/ability targets the warded permanent.
///
/// The ward cost is modelled as an arbitrary <see cref="ICost"/>
/// (<see cref="PaymentCost"/>) so the same primitive covers <b>mana ward</b>
/// (Ward {4} — Kappa Cannoneer), <b>discard ward</b> (Ward—Discard a card —
/// Reality Smasher, Graveyard Trespasser), <b>pay-life ward</b> (Ward—Pay N
/// life — Sire of Seven Deaths), and <b>sacrifice ward</b> (CR 702.21c)
/// without a separate primitive per cost shape (CR 702.21 generality).
///
/// For backward compatibility the mana portion is still surfaced as a
/// <see cref="Majik.Core.ValueObjects.ManaCost"/> via <see cref="Cost"/> —
/// for non-mana wards it is <see cref="Majik.Core.ValueObjects.ManaCost.Zero"/>
/// and the real payment lives in <see cref="PaymentCost"/>.
/// </summary>
public sealed class WardEffect
{
    /// <summary>The warded permanent (the protected target).</summary>
    public Permanent Source { get; }

    /// <summary>
    /// The mana portion of the ward cost (CR 702.21). For a non-mana ward
    /// (discard / pay-life / sacrifice) this is
    /// <see cref="Majik.Core.ValueObjects.ManaCost.Zero"/> — read
    /// <see cref="PaymentCost"/> for the actual payment. Preserved as a typed
    /// <see cref="Majik.Core.ValueObjects.ManaCost"/> so existing Ward {N}
    /// inspection sites keep working.
    /// </summary>
    public Majik.Core.ValueObjects.ManaCost Cost { get; }

    /// <summary>
    /// The full ward cost the targeting player must pay or have the
    /// spell/ability countered (CR 702.21) — any <see cref="ICost"/>:
    /// <see cref="ManaCostCost"/>, <see cref="DiscardACardCost"/>,
    /// <see cref="PayLifeCost"/>, <see cref="SacrificeSelfCost"/>, … This is
    /// what <see cref="Resolve"/> charges. For a mana ward it wraps
    /// <see cref="Cost"/>.
    /// </summary>
    public ICost PaymentCost { get; }

    /// <summary>
    /// CR 702.21 — mana-ward overload (Ward {N}). Wraps the supplied
    /// <see cref="Majik.Core.ValueObjects.ManaCost"/> as both the inspectable
    /// <see cref="Cost"/> and the chargeable <see cref="PaymentCost"/>.
    /// Preserves the original Ward {N} call sites (the per-card
    /// <c>BuildWardEffect</c> helpers).
    /// </summary>
    public WardEffect(Permanent source, Majik.Core.ValueObjects.ManaCost cost)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Cost = cost ?? throw new ArgumentNullException(nameof(cost));
        PaymentCost = new ManaCostCost(cost);
    }

    /// <summary>
    /// CR 702.21 — non-mana (or arbitrary) ward. The mana portion is
    /// <see cref="Majik.Core.ValueObjects.ManaCost.Zero"/> and the real
    /// payment is the supplied <paramref name="paymentCost"/>
    /// (<see cref="DiscardACardCost"/>, <see cref="PayLifeCost"/>,
    /// <see cref="SacrificeSelfCost"/>, …).
    /// </summary>
    public WardEffect(Permanent source, ICost paymentCost)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        PaymentCost = paymentCost ?? throw new ArgumentNullException(nameof(paymentCost));
        // If the supplied cost is itself a mana cost, surface its mana portion;
        // otherwise the inspectable mana portion is Zero (non-mana ward).
        Cost = paymentCost is ManaCostCost manaCost
            ? manaCost.Cost
            : Majik.Core.ValueObjects.ManaCost.Zero;
    }

    /// <summary>
    /// CR 702.21 — does <paramref name="caster"/> have to pay the ward cost?
    /// Ward only triggers off a spell/ability an OPPONENT of the warded
    /// permanent's controller controls (CR 702.21e). When the targeting
    /// player controls the permanent, Ward does not apply (returns false).
    /// </summary>
    public bool Applies(Player caster)
    {
        if (caster == null) return false;
        return !ReferenceEquals(Source.Controller, caster);
    }

    /// <summary>
    /// Pre-decision overload (legacy shape): given whether the caster paid the
    /// ward cost, return true if the spell/ability is countered. Ward only
    /// triggers on opponent-controlled spells/abilities; returns false when the
    /// targeting player controls the warded permanent.
    /// </summary>
    public bool ResolvesWard(Player caster, bool casterPaidWardCost)
    {
        if (!Applies(caster)) return false;
        return !casterPaidWardCost;
    }

    /// <summary>
    /// CR 702.21f — resolve the ward against <paramref name="caster"/> (the
    /// opponent who targeted the warded permanent). If the caster controls the
    /// warded permanent, Ward does not apply and nothing is countered (returns
    /// false). Otherwise the caster pays the ward <see cref="PaymentCost"/> if
    /// they can and choose to; <paramref name="payIfAble"/> models that choice
    /// (default true — pay when able, the rational play to keep the spell).
    /// Returns true when the spell/ability should be COUNTERED — i.e. the cost
    /// was not paid (either unaffordable or declined).
    ///
    /// When the cost IS paid this mutates game state via
    /// <see cref="ICost.Pay"/> (discards the card / loses the life /
    /// sacrifices the permanent), exactly as a real ward resolution would.
    /// </summary>
    public bool Resolve(Player caster, bool payIfAble = true)
    {
        if (!Applies(caster)) return false;

        // CR 702.21f — "unless its controller pays {cost}". The cost is paid
        // only when the player both CAN pay and CHOOSES to.
        if (payIfAble && PaymentCost.CanPay(caster))
        {
            PaymentCost.Pay(caster);
            return false; // paid → not countered.
        }

        return true; // not paid → countered.
    }
}
