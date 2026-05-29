using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// Activates a player's chosen mana sources (lands, mana abilities), adds the
/// generated mana into the player's pool, then attempts to pay the cost
/// from the pool. Atomic: if cost can't be paid, no sources are tapped.
/// </summary>
public sealed class ManaPaymentResolver
{
    private readonly ContinuousEffectsService? _layers;

    /// <summary>
    /// Construct a resolver. When <paramref name="layers"/> is supplied,
    /// each source's mana abilities are derived via
    /// <see cref="EffectiveManaAbilities.For"/> so CR 305.6 retyping
    /// (Blood Moon, Spreading Seas, etc.) is honoured. When null, the
    /// resolver falls back to the printed mana abilities — preserves
    /// behaviour for callers (and tests) that don't have a layer service.
    /// </summary>
    public ManaPaymentResolver(ContinuousEffectsService? layers = null)
    {
        _layers = layers;
    }

    public bool Pay(Player payer, ManaCost cost, ManaPayment payment) =>
        Pay(payer, cost, payment, out _);

    /// <summary>
    /// Portal "Auto-pay" support. The portal's mana-cost prompt offers an
    /// Auto-pay button that returns a <see cref="ManaPayment"/> with an EMPTY
    /// source list, meaning "tap my untapped lands for me". Greedily selects
    /// untapped mana sources <paramref name="payer"/> controls so that —
    /// combined with the player's current floating pool — the
    /// <paramref name="cost"/> can be paid, and returns them packaged as a
    /// <see cref="ManaPayment"/> (no source is tapped here; the caller pays
    /// via <see cref="Pay(Player, ManaCost, ManaPayment)"/>, which simulates
    /// before committing).
    ///
    /// <para>Hybrid / Phyrexian pips need an explicit player choice and are
    /// out of scope: those costs return <c>false</c> with
    /// <see cref="ManaPayment.Empty"/> (leaving the existing prompt-driven
    /// behaviour intact). Returns <c>false</c> when the floating pool plus
    /// the available untapped sources still can't cover the cost.</para>
    ///
    /// <para>Mana abilities are derived via
    /// <see cref="EffectiveManaAbilities.For"/> (the same path
    /// <see cref="Pay(Player, ManaCost, ManaPayment, out IReadOnlyList{ValueObjects.ManaColor})"/>
    /// uses) so CR 305.6 retyping (Blood Moon, Spreading Seas) and
    /// summoning-sickness gating (CR 605.3a — creature mana sources) are
    /// honoured. Lands aren't summoning-sick, so their abilities activate
    /// freely.</para>
    /// </summary>
    public bool TryAutoSelectSources(Player payer, ManaCost cost, out ManaPayment payment)
    {
        if (payer == null) throw new ArgumentNullException(nameof(payer));
        if (cost == null) throw new ArgumentNullException(nameof(cost));

        payment = ManaPayment.Empty;

        // Hybrid / Phyrexian pips require explicit player input — leave the
        // current prompt-driven behaviour. Out of scope for auto-tap.
        if (cost.HybridPips.Count > 0 || cost.PhyrexianPips.Count > 0)
        {
            return false;
        }

        var pool = payer.ManaPool;

        // Colored shortfall after the floating pool absorbs what it can.
        int needW = Math.Max(0, cost.White - pool.White);
        int needU = Math.Max(0, cost.Blue  - pool.Blue);
        int needB = Math.Max(0, cost.Black - pool.Black);
        int needR = Math.Max(0, cost.Red   - pool.Red);
        int needG = Math.Max(0, cost.Green - pool.Green);

        // Generic still needed after the pool's leftover (the floating mana
        // not consumed by colored pips) is applied. CanPay already short-
        // circuits the all-from-pool case at the call site, but compute the
        // generic shortfall precisely so we don't over-select sources.
        int poolUsedForColored =
            Math.Min(pool.White, cost.White) + Math.Min(pool.Blue, cost.Blue) +
            Math.Min(pool.Black, cost.Black) + Math.Min(pool.Red, cost.Red) +
            Math.Min(pool.Green, cost.Green);
        int poolLeftoverForGeneric = pool.Total - poolUsedForColored;
        int needGeneric = Math.Max(0, cost.Generic - poolLeftoverForGeneric);

        // Candidate untapped mana sources the player controls.
        var candidates = new List<(ICard Card, IReadOnlyList<IManaAbility> Abilities)>();
        foreach (var card in payer.Zones.Battlefield.GetCards())
        {
            if (card is not Permanent perm) continue;
            if (perm.IsTapped) continue;

            var abilities = EffectiveManaAbilities.For(perm, _layers)
                .Where(a => a.CanActivate())
                .ToList();
            if (abilities.Count == 0) continue;

            candidates.Add((card, abilities));
        }

        var selected = new List<ICard>();
        var used = new HashSet<ICard>();

        // 1) Satisfy each needed colored pip with a source that can produce it.
        bool TrySelectFor(Func<ManaCost, int> colorOf, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var pick = candidates.FirstOrDefault(c =>
                    !used.Contains(c.Card) &&
                    c.Abilities.Any(a => colorOf(a.ManaGenerated) > 0));
                if (pick.Card == null) return false;
                used.Add(pick.Card);
                selected.Add(pick.Card);
            }
            return true;
        }

        if (!TrySelectFor(m => m.White, needW)) return false;
        if (!TrySelectFor(m => m.Blue,  needU)) return false;
        if (!TrySelectFor(m => m.Black, needB)) return false;
        if (!TrySelectFor(m => m.Red,   needR)) return false;
        if (!TrySelectFor(m => m.Green, needG)) return false;

        // 2) Cover the remaining generic with any unused untapped source.
        for (int i = 0; i < needGeneric; i++)
        {
            var pick = candidates.FirstOrDefault(c => !used.Contains(c.Card));
            if (pick.Card == null) return false;
            used.Add(pick.Card);
            selected.Add(pick.Card);
        }

        var candidate = new ManaPayment(selected);

        // Verify the selection (with the floating pool) can actually pay,
        // WITHOUT committing anything (nothing is tapped here — the caller
        // pays via Pay, which simulates before committing). Mirror Pay's
        // greedy per-source ability choice so dual / any-color sources pick
        // the ability whose color is still needed rather than blindly the
        // first one.
        var remaining = new Dictionary<char, int>
        {
            ['W'] = cost.White, ['U'] = cost.Blue, ['B'] = cost.Black,
            ['R'] = cost.Red,   ['G'] = cost.Green,
        };
        var simulated = pool;
        foreach (var card in selected)
        {
            var abilities = candidates.First(c => ReferenceEquals(c.Card, card)).Abilities;
            var ability = abilities[0];
            foreach (var opt in abilities)
            {
                var mana = opt.ManaGenerated;
                char? satisfies = null;
                if (remaining['W'] > 0 && mana.White > 0) satisfies = 'W';
                else if (remaining['U'] > 0 && mana.Blue > 0) satisfies = 'U';
                else if (remaining['B'] > 0 && mana.Black > 0) satisfies = 'B';
                else if (remaining['R'] > 0 && mana.Red > 0) satisfies = 'R';
                else if (remaining['G'] > 0 && mana.Green > 0) satisfies = 'G';
                if (satisfies.HasValue)
                {
                    ability = opt;
                    remaining[satisfies.Value]--;
                    break;
                }
            }
            simulated = simulated.Add(ability.ManaGenerated);
        }
        if (!simulated.CanPay(cost))
        {
            return false;
        }

        payment = candidate;
        return true;
    }

    /// <summary>
    /// CR 702.44b — overload that also reports the distinct colors of
    /// mana actually spent on this payment. "Spent" = the colored portion
    /// of the player's pool that was consumed (colored pips + colored
    /// mana used to cover generic), computed by diffing pool-before-spend
    /// (pre-existing-floating-mana + mana produced by tapped sources)
    /// against pool-after-spend. Empty when payment failed OR when no
    /// colored mana was spent (e.g. paying {2} entirely from generic
    /// floating mana). Order is deterministic WUBRG to match the engine's
    /// canonical color iteration order.
    /// </summary>
    public bool Pay(
        Player payer,
        ManaCost cost,
        ManaPayment payment,
        out IReadOnlyList<ValueObjects.ManaColor> colorsSpent)
    {
        colorsSpent = Array.Empty<ValueObjects.ManaColor>();
        if (payer == null) throw new ArgumentNullException(nameof(payer));
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        if (payment == null) throw new ArgumentNullException(nameof(payment));

        // Pick the best ability per source given the cost. Dual / any-colour
        // lands (Sacred Foundry, Mox Opal) bind multiple ManaAbility options;
        // picking First() blindly short-pays when the bot picked the source
        // for a colour the first ability doesn't produce. Greedy: for each
        // source, choose the ability whose generated colour is still needed
        // (W, U, B, R, G in cost order); fall back to the first ability
        // when no coloured need matches.
        var remaining = new Dictionary<char, int>
        {
            ['W'] = cost.White, ['U'] = cost.Blue, ['B'] = cost.Black,
            ['R'] = cost.Red,   ['G'] = cost.Green,
        };
        var abilities = new List<IManaAbility>(payment.Sources.Count);
        foreach (var src in payment.Sources)
        {
            // CR 305.6 — when a Layer 4 retyping effect has changed the
            // source's land subtypes (Blood Moon, Spreading Seas, …),
            // EffectiveManaAbilities substitutes basic mana abilities
            // for the printed ones. Otherwise prints are returned as-is.
            // Null _layers ⇒ printed path (legacy/tests).
            var options = src is Permanent perm
                ? EffectiveManaAbilities.For(perm, _layers).ToList()
                : src.Abilities.OfType<IManaAbility>().ToList();
            if (options.Count == 0)
                throw new InvalidOperationException($"{src.Name} has no mana ability.");

            IManaAbility picked = options[0];
            foreach (var opt in options)
            {
                var mana = opt.ManaGenerated;
                char? satisfies = null;
                if (remaining['W'] > 0 && mana.White > 0) satisfies = 'W';
                else if (remaining['U'] > 0 && mana.Blue > 0) satisfies = 'U';
                else if (remaining['B'] > 0 && mana.Black > 0) satisfies = 'B';
                else if (remaining['R'] > 0 && mana.Red > 0) satisfies = 'R';
                else if (remaining['G'] > 0 && mana.Green > 0) satisfies = 'G';
                if (satisfies.HasValue)
                {
                    picked = opt;
                    remaining[satisfies.Value]--;
                    break;
                }
            }
            abilities.Add(picked);
        }

        // Simulate adding mana into a copy of the pool to verify the cost
        // is payable BEFORE we tap anything.
        var simulated = payer.ManaPool;
        var produced = new List<ManaCost>(abilities.Count);
        foreach (var ab in abilities)
        {
            // ManaAbility's pre-built ctor stores the cost on ManaGenerated.
            produced.Add(ab.ManaGenerated);
            simulated = simulated.Add(ab.ManaGenerated);
        }

        var (_, canPay) = simulated.Pay(cost);
        if (!canPay)
        {
            return false;
        }

        // Snapshot the player's pool BEFORE we tap producers + pay, so
        // we can diff the colored buckets post-spend to compute Sunburst-
        // style "colors of mana spent" (CR 702.44b). Pool-before-spend +
        // produced mana = pool-with-sources; subtract pool-after-pay to
        // get the actually-consumed delta per color.
        var poolBefore = payer.ManaPool;

        // Commit: actually tap each source and add to real pool, then pay.
        foreach (var ab in abilities)
        {
            ab.Activate();
        }
        foreach (var p in produced)
        {
            payer.AddManaToPool(p);
        }
        var ok = payer.PayMana(cost);
        if (!ok) return false;

        var poolAfter = payer.ManaPool;
        var availableW = poolBefore.White;
        var availableU = poolBefore.Blue;
        var availableB = poolBefore.Black;
        var availableR = poolBefore.Red;
        var availableG = poolBefore.Green;
        foreach (var p in produced)
        {
            availableW += p.White;
            availableU += p.Blue;
            availableB += p.Black;
            availableR += p.Red;
            availableG += p.Green;
        }
        var spent = new List<ValueObjects.ManaColor>(5);
        if (availableW - poolAfter.White > 0) spent.Add(ValueObjects.ManaColor.White);
        if (availableU - poolAfter.Blue  > 0) spent.Add(ValueObjects.ManaColor.Blue);
        if (availableB - poolAfter.Black > 0) spent.Add(ValueObjects.ManaColor.Black);
        if (availableR - poolAfter.Red   > 0) spent.Add(ValueObjects.ManaColor.Red);
        if (availableG - poolAfter.Green > 0) spent.Add(ValueObjects.ManaColor.Green);
        colorsSpent = spent;
        return true;
    }
}
