using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.169 — Offspring {cost}. "Offspring is a keyword that represents two
/// abilities" (CR 702.169a): a static ability that lets the caster pay an
/// additional cost as the spell is cast, and a triggered ability of the
/// resulting permanent. The first half is modelled here:
///
/// <para>CR 702.169a — "Offspring {cost}" on a creature spell means "You may
/// pay an additional {cost} as you cast this spell." This is an
/// <em>optional</em> additional cost (CR 601.2f / 118.9 — additional, not
/// alternative: the printed mana cost is still paid in full, plus this cost on
/// top when the caster chooses to). The caster decides at announcement whether
/// to layer it (CR 601.2b — locked in then).</para>
///
/// <para>CR 702.169b — "If you pay this cost, when the permanent this spell
/// becomes enters, that permanent's controller creates a token that's a copy
/// of it, except it's 1/1." That ETB token-copy is wired by the resolving
/// permanent's <see cref="Majik.Core.Keywords.OffspringAbility"/> trigger,
/// which reads the <see cref="Card.WasOffspringPaid"/> sentinel this cost
/// stamps.</para>
///
/// <para>Implemented as an <see cref="IAdditionalCost"/> the caller layers onto
/// a cast via <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// <c>additionalCosts</c> list — the same shape as
/// <see cref="MultikickerAdditionalCost"/> / <see cref="BargainAdditionalCost"/>,
/// but a single yes/no mana payment that stamps a boolean sentinel rather than
/// a multiplicity. Because Offspring is optional, this cost is only added to the
/// cast when the caster chooses to pay it; <see cref="Pay"/> drains the
/// additional mana and stamps the spell so its ETB trigger fires.</para>
///
/// <para>The Offspring sentinel is deliberately NOT cleared by SpellCastFlow's
/// resolution cleanup (unlike Kicker / Bargain). The flag is read AFTER the
/// creature has entered the battlefield, by its ETB trigger off the stack
/// (CR 603.6b) — clearing it during the creature spell's own resolution would
/// zero it before the ETB ever reads it. The ETB trigger clears it itself.</para>
/// </summary>
public sealed class OffspringAdditionalCost : IAdditionalCost
{
    private readonly ICard _card;
    private readonly ManaCost _cost;

    /// <param name="card">The creature spell being cast.</param>
    /// <param name="cost">The Offspring additional mana cost (Manifold
    /// Mouse / Pawpatch Recruit — {2}).</param>
    public OffspringAdditionalCost(ICard card, ManaCost cost)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _cost = cost ?? throw new ArgumentNullException(nameof(cost));
    }

    /// <summary>The Offspring additional cost ({cost}).</summary>
    public ManaCost Cost => _cost;

    public ICard Card => _card;

    public string Description => $"Offspring {_cost}";

    /// <summary>
    /// CR 702.169a / CR 601.2g — payable when the caster can produce the
    /// Offspring mana from the current pool. Checked against a non-destructive
    /// copy of the pool so a shortfall is caught before any mana is committed.
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        var (_, success) = caster.ManaPool.Pay(_cost);
        return success;
    }

    /// <summary>
    /// CR 702.169a / CR 601.2f — pay the Offspring additional mana and stamp
    /// the spell's <see cref="Card.WasOffspringPaid"/> sentinel so its ETB
    /// trigger creates the 1/1 token copy (CR 702.169b). Affordability is
    /// verified first (CR 601.2g — no partial payment leaks into resolution);
    /// only then is the payment committed and the card stamped. Returns false
    /// (no stamp, pool untouched) when the cost can't be paid.
    /// </summary>
    public bool Pay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (!CanPay(caster)) return false;
        if (!caster.PayMana(_cost)) return false;

        if (_card is Card concrete) concrete.SetWasOffspringPaid(true);
        return true;
    }
}
