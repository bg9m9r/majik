using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.33 — Kicker. Optional <em>additional</em> cost on top of a
/// spell's mana cost. The caster announces whether they want to pay the
/// kicker (CR 601.2b — modes/optional costs locked in at announcement),
/// and the cast pipeline branches the spell's printed body on the
/// "was kicked?" sentinel at resolution (CR 702.33b — "if [spell] was
/// kicked, …").
///
/// <para>
/// Implemented as an <see cref="IAdditionalCost"/> the caller layers
/// onto a cast via <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// <c>additionalCosts</c> list (Buyback / sacrifice-rider shape).
/// <see cref="Pay"/> pays the kicker mana and stamps
/// <see cref="Card.WasKicked"/> on the cast card so the resolve body
/// can branch via <c>chosen.AdditionalCostPayments</c> OR the cheaper
/// <c>card.WasKicked</c> read. The flag is cleared at the end of
/// resolution by <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// cleanup-effect wrapper so a re-cast / blink / token copy doesn't
/// inherit the prior posture (CR 400.7).
/// </para>
///
/// <para>
/// Kicker is <em>not</em> an alternative cost (CR 118.9): the printed
/// mana cost is still paid in full, plus the kicker on top. Pitch /
/// Flashback / Evoke / Overload all replace the printed cost; kicker
/// stacks on it.
/// </para>
/// </summary>
public sealed class KickerAdditionalCost : IAdditionalCost
{
    private readonly ICard _card;
    private readonly ManaCost _kickerCost;

    public KickerAdditionalCost(ICard card, ManaCost kickerCost)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _kickerCost = kickerCost ?? throw new ArgumentNullException(nameof(kickerCost));
    }

    public ManaCost KickerCost => _kickerCost;
    public ICard Card => _card;

    public string Description => $"Kicker {_kickerCost}";

    /// <summary>
    /// CR 702.33 — Kicker is always optional. Legality reduces to
    /// "can the caster produce the kicker mana?". The caller decides
    /// whether to layer the cost; if it's layered, the pipeline must
    /// pay it (no partial / skip semantics inside the additional-cost
    /// loop — skipping means not layering it).
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        return caster.ManaPool.Pay(_kickerCost).Success;
    }

    /// <summary>
    /// CR 702.33 / CR 601.2f — pay the kicker mana and stamp the
    /// resolving card so the resolve body's "if [spell] was kicked"
    /// branch (CR 702.33b) fires. Returns true on success; on failure
    /// the card is NOT stamped (no half-paid kicker leaks into the
    /// resolution branch).
    /// </summary>
    public bool Pay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (!caster.PayMana(_kickerCost)) return false;
        if (_card is Card concrete)
        {
            concrete.SetWasKicked(true);
        }
        return true;
    }
}
