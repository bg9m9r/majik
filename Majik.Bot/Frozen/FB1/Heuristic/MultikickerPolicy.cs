using Majik.Core.ValueObjects;

namespace Majik.Bot.Frozen.FB1;

/// <summary>
/// Picks how many times the bot pays a Multikicker cost (CR 702.32a — the
/// caster chooses the number of times the kicker is paid as the spell is
/// announced, CR 601.2b, bounded by available mana). This is the bot-side
/// counterpart to the already-built engine primitive
/// <see cref="Majik.Core.Costs.MultikickerAdditionalCost"/> +
/// <see cref="Majik.Core.Cards.Card.TimesKicked"/>: the engine resolves a
/// chosen count, but nothing in the EV-search policy decided what that count
/// should be. This closes that gap.
///
/// <para>The heuristic is the canonical iterated additional-cost loop the
/// rest of the policy layer uses for kicker / X: <em>pay one more kick while
/// (a) the next kick is still affordable from the mana left over after the
/// base spell cost AND (b) the EV gate says the extra kick is worth it.</em>
/// The default EV gate is monotone-positive — Everflowing Chalice, the
/// canonical Multikicker payoff, banks one charge counter (one future
/// colourless mana source, CR 122 / CR 614.1d) per kick, so every affordable
/// kick strictly improves the board and the bot spends all the mana it can.
/// Callers with a saturating payoff (a multikicked spell whose marginal value
/// tapers) pass a <paramref name="kickIsWorthIt"/> predicate to cap the loop
/// below the mana floor.</para>
///
/// <para>Pure function of (per-kick cost, mana available, optional EV gate) —
/// no engine mutation, no <see cref="Majik.Core.Players.Player"/> handle —
/// mirroring the closed-form projection style of
/// <see cref="ModalPolicy.PickX"/> and
/// <see cref="ActivatedAbilityPolicy"/>. The caller layers the result onto the
/// cast via <c>EverflowingChaliceFactory.BuildAdditionalCost(card, times)</c>
/// →
/// <see cref="Majik.Core.Players.Agents.PriorityAction.CastSpell.AdditionalCosts"/>.</para>
/// </summary>
internal static class MultikickerPolicy
{
    /// <summary>
    /// CR 702.32a — choose how many times to pay <paramref name="perKickCost"/>.
    /// </summary>
    /// <param name="perKickCost">The mana cost of a single kick (Everflowing
    /// Chalice — {2}). A free / null cost yields 0 (guards against an infinite
    /// multikick on a {0} kicker).</param>
    /// <param name="manaAvailable">Generic-mana budget left AFTER paying the
    /// spell's base cost — the ceiling on total kick spend. Each kick consumes
    /// <c>perKickCost.TotalValue</c> of it.</param>
    /// <param name="kickIsWorthIt">EV gate, evaluated per prospective kick:
    /// given the count we'd reach by paying once more (1-based), return whether
    /// that additional kick improves the bot's position. Defaults to "always"
    /// — every affordable kick is taken (monotone-positive ramp payoff). The
    /// loop stops the first time it returns false, so the gate may model a
    /// saturating curve.</param>
    /// <returns>The number of times to pay the kicker (0..N), the smaller of
    /// the affordable count and the EV ceiling. 0 = decline (legal — the spell
    /// is cast un-kicked).</returns>
    public static int ChooseTimes(
        ManaCost perKickCost,
        int manaAvailable,
        Func<int, bool>? kickIsWorthIt = null)
    {
        if (perKickCost is null) return 0;

        var perKick = perKickCost.TotalValue;
        // A {0} (or otherwise free) kicker has no affordability bound; without
        // a per-kick mana drain the "pay while mana remains" loop would never
        // terminate. The bot declines rather than multikick unboundedly — a
        // free-multikicker payoff would need its own bespoke policy.
        if (perKick <= 0) return 0;
        if (manaAvailable < perKick) return 0;

        var gate = kickIsWorthIt ?? (_ => true);

        var times = 0;
        var remaining = manaAvailable;
        // Iterated "pay once more while mana remains AND EV improves"
        // (CR 702.32a). The gate is consulted with the count we'd land on by
        // committing this kick, so a saturating payoff can refuse the marginal
        // kick before the mana floor is reached.
        while (remaining >= perKick && gate(times + 1))
        {
            times++;
            remaining -= perKick;
        }

        return times;
    }
}
