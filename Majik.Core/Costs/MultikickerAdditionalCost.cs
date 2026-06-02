using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.32 — Multikicker. A kicker ability (CR 702.33) that "may be paid
/// any number of times" as the spell is cast. The caster chooses how many
/// times to pay the kicker cost at announcement (CR 601.2b — the number of
/// times is locked in then), and the spell's resolution scales on the count
/// (CR 702.32c — "if a spell was kicked N times, …"). Everflowing Chalice
/// ("enters with a charge counter on it for each time it was kicked") is the
/// canonical scaling payoff.
///
/// <para>
/// Implemented as an <see cref="IAdditionalCost"/> the caller layers onto a
/// cast via <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// <c>additionalCosts</c> list — the same shape as
/// <see cref="KickerAdditionalCost"/>, but parameterized by a
/// <see cref="Times"/> multiplicity instead of a single yes/no kick. The
/// caller (bot decision layer, UI, scripted test) decides how many times to
/// pay — bounded by available mana — and constructs this with that count.
/// </para>
///
/// <para>
/// <see cref="Pay"/> drains the per-kick mana cost <see cref="Times"/> times
/// and stamps <see cref="Card.SetTimesKicked"/> on the cast card so the
/// resolve body / ETB rider can read the count via
/// <c>card.TimesKicked</c> (and the binary <c>card.WasKicked</c> for
/// "was it kicked at all?"). The count is cleared at end of resolution by
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s Kicker cleanup wrapper so a
/// re-cast / blink / token copy doesn't inherit the prior posture (CR 400.7).
/// </para>
///
/// <para>
/// Like Kicker, Multikicker is an <em>additional</em> cost (CR 601.2f), not
/// an alternative cost (CR 118.9): the printed mana cost is still paid in
/// full, plus the kicker cost once per kick on top. A <see cref="Times"/> of
/// 0 means "declined to multikick" — legal, no mana spent, the spell resolves
/// with <c>TimesKicked == 0</c>.
/// </para>
/// </summary>
public sealed class MultikickerAdditionalCost : IAdditionalCost
{
    private readonly ICard _card;
    private readonly ManaCost _perKickCost;

    /// <param name="card">The spell being cast.</param>
    /// <param name="perKickCost">The mana cost of a single kick (Everflowing
    /// Chalice — {2}).</param>
    /// <param name="times">How many times the caster chose to pay the kicker
    /// (0..N, bounded by available mana by the caller). 0 is legal — the spell
    /// is cast without any kick.</param>
    public MultikickerAdditionalCost(ICard card, ManaCost perKickCost, int times)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _perKickCost = perKickCost ?? throw new ArgumentNullException(nameof(perKickCost));
        if (times < 0) throw new ArgumentOutOfRangeException(nameof(times));
        Times = times;
    }

    /// <summary>How many times the caster chose to pay the kicker (CR 702.32a).</summary>
    public int Times { get; }

    public ManaCost PerKickCost => _perKickCost;
    public ICard Card => _card;

    public string Description =>
        Times <= 0 ? "Multikicker (not paid)" : $"Multikicker {_perKickCost} ×{Times}";

    /// <summary>
    /// CR 702.32 — legality reduces to "can the caster produce the kicker
    /// mana <see cref="Times"/> times?". A <see cref="Times"/> of 0 is always
    /// payable (nothing to pay). Simulates the N successive per-kick payments
    /// against a non-destructive copy of the pool so an aggregate shortfall
    /// is caught before any mana is committed.
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (Times <= 0) return true;

        var pool = caster.ManaPool;
        for (var i = 0; i < Times; i++)
        {
            var (next, success) = pool.Pay(_perKickCost);
            if (!success) return false;
            pool = next;
        }
        return true;
    }

    /// <summary>
    /// CR 702.32 / CR 601.2f — pay the kicker mana <see cref="Times"/> times
    /// and stamp the resolving card's kick count so the resolve body / ETB
    /// rider (Everflowing Chalice's "for each time it was kicked", CR 702.32c)
    /// can scale on it. A <see cref="Times"/> of 0 pays nothing, stamps a
    /// count of 0, and returns true. The full aggregate is affordability-
    /// checked first (CR 601.2g — no partially-paid multikicker leaks into
    /// resolution); only then are the N per-kick payments committed and the
    /// card stamped.
    /// </summary>
    public bool Pay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));

        if (Times <= 0)
        {
            if (_card is Card zero) zero.SetTimesKicked(0);
            return true;
        }

        // CR 601.2g — verify the whole multikicker is affordable before
        // committing any of the N payments, so the pool can't be left
        // half-drained on a shortfall.
        if (!CanPay(caster)) return false;

        for (var i = 0; i < Times; i++)
        {
            if (!caster.PayMana(_perKickCost)) return false;
        }

        if (_card is Card concrete)
        {
            concrete.SetTimesKicked(Times);
        }
        return true;
    }
}
