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
    /// CR 609.4b — overload carrying the "you may spend mana as though it were
    /// mana of any color" permission (Robber of the Rich's stolen-card cast,
    /// Fist of Suns, Cascading Cataracts). When
    /// <paramref name="spendAsAnyColor"/> is <c>true</c>, every colored pip of
    /// <paramref name="cost"/> is treated as a generic requirement for the
    /// purposes of color matching, payability and the actual spend — so any
    /// mana of any color (and any generic) qualifies (CR 106.6 — the
    /// permission widens which mana satisfies the cost; it does NOT reduce the
    /// cost's mana value). The Sunburst colors-spent ledger still reports the
    /// real colors of mana consumed.
    /// </summary>
    public bool Pay(Player payer, ManaCost cost, ManaPayment payment, bool spendAsAnyColor) =>
        Pay(payer, cost, payment, spentOn: null, spendAsAnyColor, out _, out _);

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
        out IReadOnlyList<ValueObjects.ManaColor> colorsSpent) =>
        Pay(payer, cost, payment, out colorsSpent, out _);

    /// <summary>
    /// Mana-provenance overload that reports BOTH the distinct colors spent
    /// (<paramref name="colorsSpent"/>, the Sunburst / CR 702.44b set) AND
    /// the per-color spent <em>count</em> (<paramref name="colorCounts"/>),
    /// preserving multiplicity so an intervening-if can tell "{R}{R} spent"
    /// from "{R}{G} spent" (the hybrid Elemental Incarnation family). The
    /// distinct set is exactly the count ledger collapsed to "> 0", so a
    /// caller that stamps the counts onto the card
    /// (<see cref="Cards.Card.SetPendingCastColorCounts"/>) gets the distinct
    /// set derived for free. Both are empty when payment failed or no colored
    /// mana was spent (e.g. {2} paid entirely from generic floating mana).
    /// Counts are keyed by color; only positive counts appear.
    /// </summary>
    public bool Pay(
        Player payer,
        ManaCost cost,
        ManaPayment payment,
        out IReadOnlyList<ValueObjects.ManaColor> colorsSpent,
        out IReadOnlyDictionary<ValueObjects.ManaColor, int> colorCounts) =>
        Pay(payer, cost, payment, spentOn: null, spendAsAnyColor: false, out colorsSpent, out colorCounts);

    /// <summary>
    /// Slot-level mana-provenance overload (CR 106.4 — deferral #1). Identical
    /// to the count-reporting overload, but additionally consumes the payer's
    /// per-color <see cref="Majik.Core.Mana.ManaProvenanceSlot"/> ledger by the
    /// same per-color spent counts and fires each consumed slot's
    /// <see cref="Majik.Core.Mana.ManaProvenanceSlot.OnSpent"/> reaction with
    /// <paramref name="spentOn"/> — the object the mana was spent on (the cast
    /// card for a spell, or <c>null</c> for an ability-activation context).
    /// This is the "if THAT mana (from this specific source) is spent on THIS
    /// spell" mechanism: a card stamps its produced mana with a reaction
    /// (Arena of Glory's exert → grant haste to a creature spell), and the
    /// reaction fires precisely when one of its tagged units pays a cost —
    /// strictly per-pip, not "the first spell after the source resolved".
    /// </summary>
    public bool Pay(
        Player payer,
        ManaCost cost,
        ManaPayment payment,
        Cards.ICard? spentOn,
        out IReadOnlyList<ValueObjects.ManaColor> colorsSpent,
        out IReadOnlyDictionary<ValueObjects.ManaColor, int> colorCounts) =>
        Pay(payer, cost, payment, spentOn, spendAsAnyColor: false, out colorsSpent, out colorCounts);

    /// <summary>
    /// Full overload — slot-level provenance (CR 106.4, deferral #1) PLUS the
    /// CR 609.4b "spend mana as though it were mana of any color" permission
    /// (deferral: spend-mana-as-any-color-permission). When
    /// <paramref name="spendAsAnyColor"/> is set, the cost's colored pips are
    /// folded into generic (<see cref="ManaCost.WithColoredFoldedToGeneric"/>)
    /// for every color-sensitive step — the per-source greedy ability pick, the
    /// payability simulation and the real spend — so any color of mana (or
    /// generic) qualifies. The Sunburst colors-spent / count ledger is computed
    /// from the actual per-color pool delta, so it still reports the true
    /// colors of mana consumed (unaffected by the permission).
    /// </summary>
    public bool Pay(
        Player payer,
        ManaCost cost,
        ManaPayment payment,
        Cards.ICard? spentOn,
        bool spendAsAnyColor,
        out IReadOnlyList<ValueObjects.ManaColor> colorsSpent,
        out IReadOnlyDictionary<ValueObjects.ManaColor, int> colorCounts)
    {
        colorsSpent = Array.Empty<ValueObjects.ManaColor>();
        colorCounts = new Dictionary<ValueObjects.ManaColor, int>();
        if (payer == null) throw new ArgumentNullException(nameof(payer));
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        if (payment == null) throw new ArgumentNullException(nameof(payment));

        // CR 609.4b — under a "spend as though any color" permission the colored
        // pips become color-agnostic generic requirements. Fold once; all the
        // color-sensitive steps below (greedy ability pick, payability sim,
        // actual PayMana) consume this relaxed cost. The Sunburst delta uses
        // the player's pool buckets, so the reported colors-spent stay accurate.
        var matchCost = spendAsAnyColor ? cost.WithColoredFoldedToGeneric() : cost;

        // Pick the best ability per source given the cost. Dual / any-colour
        // lands (Sacred Foundry, Mox Opal) bind multiple ManaAbility options;
        // picking First() blindly short-pays when the bot picked the source
        // for a colour the first ability doesn't produce. Greedy: for each
        // source, choose the ability whose generated colour is still needed
        // (W, U, B, R, G in cost order); fall back to the first ability
        // when no coloured need matches.
        var remaining = new Dictionary<char, int>
        {
            ['W'] = matchCost.White, ['U'] = matchCost.Blue, ['B'] = matchCost.Black,
            ['R'] = matchCost.Red,   ['G'] = matchCost.Green,
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

        var (_, canPay) = simulated.Pay(matchCost);
        if (!canPay)
        {
            return false;
        }

        // CR 106.4 — spend-restriction gate. Some mana carries a "spend this
        // mana only to cast a creature spell" (Ancient Ziggurat), "…of the
        // chosen type" (Cavern of Souls), or "…on Eldrazi spells" (Eldrazi
        // Temple) rider. Such a unit is UNAVAILABLE to pay this cost unless
        // the object being paid for (spentOn) satisfies the restriction. The
        // pool buckets colour counts, so the per-slot restriction lives in the
        // provenance ledger (existing floating slots) plus the produced mana of
        // any restricted ability in THIS payment. Recompute payability with the
        // blocked colored units removed; if it no longer covers the cost,
        // reject atomically (nothing has been tapped yet).
        var spellContext = spentOn != null
            ? new Spells.Spell(spentOn, payer)
            : null;
        var blockedW = CountBlocked(payer, abilities, ValueObjects.ManaColor.White, spellContext);
        var blockedU = CountBlocked(payer, abilities, ValueObjects.ManaColor.Blue, spellContext);
        var blockedB = CountBlocked(payer, abilities, ValueObjects.ManaColor.Black, spellContext);
        var blockedR = CountBlocked(payer, abilities, ValueObjects.ManaColor.Red, spellContext);
        var blockedG = CountBlocked(payer, abilities, ValueObjects.ManaColor.Green, spellContext);
        // CR 106.1b — colorless ({C}) restricted units (Karn, Legacy Reforged's
        // "can't be spent to cast nonartifact spells") live in the Generic
        // pool bucket. Count + withhold them from Generic the same way.
        var blockedC = CountBlocked(payer, abilities, ValueObjects.ManaColor.Colorless, spellContext);
        var hasBlocked = blockedW + blockedU + blockedB + blockedR + blockedG + blockedC > 0;
        if (hasBlocked)
        {
            var spendable = simulated.RemoveColored(
                white: blockedW, blue: blockedU, black: blockedB,
                red: blockedR, green: blockedG, colorless: blockedC);
            var (_, canPaySpendable) = spendable.Pay(matchCost);
            if (!canPaySpendable)
            {
                return false;
            }
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
        // CR 106.4 — stamp every produced unit with its ability's spend-
        // restriction (and any provenance reaction) so the ledger records
        // which floating units are restricted. Restriction-only abilities
        // (Ancient Ziggurat) record slots even without an OnSpent callback;
        // ManaAbilityActivator already stamps reaction slots on the float-
        // then-cast path, so this covers the source-tapped-at-pay path.
        for (var i = 0; i < abilities.Count; i++)
        {
            var concrete = abilities[i] as Abilities.ManaAbility;
            var restriction = concrete?.SpendRestriction;
            var reaction = concrete?.ProvenanceReaction;
            if (restriction != null || reaction != null)
            {
                payer.AddManaToPool(
                    produced[i],
                    provenanceSource: abilities[i],
                    onSpent: reaction,
                    restriction: restriction);
            }
            else
            {
                payer.AddManaToPool(produced[i]);
            }
        }

        // CR 106.4 — withhold the blocked restricted colored mana from the pool
        // across the actual payment so the bucketed ManaPool.Pay (which has no
        // per-slot view and would otherwise greedily spend a restricted unit on
        // a generic pip) can only consume SPENDABLE mana. The withheld mana is
        // restored afterward and stays floating, its provenance slots intact.
        if (hasBlocked)
        {
            payer.WithholdColoredMana(blockedW, blockedU, blockedB, blockedR, blockedG, colorless: blockedC);
        }
        var ok = payer.PayMana(matchCost);
        if (hasBlocked)
        {
            payer.RestoreColoredMana(blockedW, blockedU, blockedB, blockedR, blockedG, colorless: blockedC);
        }
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
        // CR 702.44b — the per-color pool delta IS the multiplicity of that
        // color's mana spent (colored pips + colored mana used to cover
        // generic). The distinct set is just this ledger collapsed to "> 0".
        // Keep BOTH: counts preserve multiplicity so "{R}{R} was spent"
        // intervening-ifs (Vibrance / Wistfulness) can distinguish {R}{R}
        // from {R}{G}; the distinct list keeps Sunburst working unchanged.
        var counts = new Dictionary<ValueObjects.ManaColor, int>(5);
        var spent = new List<ValueObjects.ManaColor>(5);
        var deltaW = availableW - poolAfter.White;
        var deltaU = availableU - poolAfter.Blue;
        var deltaB = availableB - poolAfter.Black;
        var deltaR = availableR - poolAfter.Red;
        var deltaG = availableG - poolAfter.Green;
        if (deltaW > 0) { spent.Add(ValueObjects.ManaColor.White); counts[ValueObjects.ManaColor.White] = deltaW; }
        if (deltaU > 0) { spent.Add(ValueObjects.ManaColor.Blue);  counts[ValueObjects.ManaColor.Blue]  = deltaU; }
        if (deltaB > 0) { spent.Add(ValueObjects.ManaColor.Black); counts[ValueObjects.ManaColor.Black] = deltaB; }
        if (deltaR > 0) { spent.Add(ValueObjects.ManaColor.Red);   counts[ValueObjects.ManaColor.Red]   = deltaR; }
        if (deltaG > 0) { spent.Add(ValueObjects.ManaColor.Green); counts[ValueObjects.ManaColor.Green] = deltaG; }
        colorsSpent = spent;
        colorCounts = counts;

        // CR 106.4 — slot-level provenance (deferral #1). Consume the payer's
        // provenance ledger by exactly the per-color spent counts, firing each
        // tagged slot's OnSpent reaction with what the mana was spent on. The
        // pool delta is the multiplicity of mana spent of that color, so we
        // pop that many matching slots FIFO — untagged spends pop nothing, and
        // a slot's reaction (e.g. Arena of Glory's haste grant) fires only
        // when one of its units actually paid.
        if (deltaW > 0) payer.ConsumeProvenanceSlotsOnSpend(ValueObjects.ManaColor.White, deltaW, spentOn);
        if (deltaU > 0) payer.ConsumeProvenanceSlotsOnSpend(ValueObjects.ManaColor.Blue,  deltaU, spentOn);
        if (deltaB > 0) payer.ConsumeProvenanceSlotsOnSpend(ValueObjects.ManaColor.Black, deltaB, spentOn);
        if (deltaR > 0) payer.ConsumeProvenanceSlotsOnSpend(ValueObjects.ManaColor.Red,   deltaR, spentOn);
        if (deltaG > 0) payer.ConsumeProvenanceSlotsOnSpend(ValueObjects.ManaColor.Green, deltaG, spentOn);

        // CR 107.4c — colorless ({C}) mana now lives in its own pool bucket and
        // its provenance slots (Karn, Legacy Reforged) track it. The colorless
        // delta is the number of colorless-bucket units the payment consumed;
        // pop that many Colorless slots FIFO that the spend satisfies, firing
        // their reactions. Untagged colorless spends pop nothing (no slots). The
        // withheld restricted colorless (blockedC) was held back across PayMana,
        // so a nonartifact spell never consumes Karn's {C} here.
        var availableColorless = poolBefore.Colorless + produced.Sum(p => p.Colorless);
        var deltaC = availableColorless - poolAfter.Colorless;
        if (deltaC > 0) payer.ConsumeProvenanceSlotsOnSpend(ValueObjects.ManaColor.Colorless, deltaC, spentOn);

        return true;
    }

    /// <summary>
    /// CR 106.4 — count the units of <paramref name="color"/> that are
    /// UNAVAILABLE for the current payment because they carry a spend-
    /// restriction the spell being cast (<paramref name="spell"/>, null for a
    /// non-spell context) does not satisfy. Two sources contribute:
    /// <list type="number">
    /// <item>floating mana already in the payer's provenance ledger
    /// (e.g. mana floated via a Cavern of Souls ability earlier this step);</item>
    /// <item>mana about to be produced by the <paramref name="abilities"/>
    /// tapped for THIS payment (e.g. Ancient Ziggurat's any-color ability) —
    /// not yet in the ledger at gate time, so counted from the ability's
    /// <see cref="Abilities.ManaAbility.SpendRestriction"/> + its generated
    /// colored amount.</item>
    /// </list>
    /// Generic mana is never restricted, so only WUBRG colors are counted.
    /// </summary>
    private static int CountBlocked(
        Player payer,
        IReadOnlyList<IManaAbility> abilities,
        ValueObjects.ManaColor color,
        Spells.ISpell? spell)
    {
        var blocked = 0;

        // (1) Already-floating restricted slots that don't satisfy the spell.
        foreach (var slot in payer.ManaProvenance)
        {
            if (slot.Color == color && slot.Restriction != null && !slot.CanSpendOn(spell))
            {
                blocked++;
            }
        }

        // (2) About-to-be-produced restricted units from this payment.
        foreach (var ab in abilities)
        {
            var restriction = (ab as Abilities.ManaAbility)?.SpendRestriction;
            if (restriction == null || restriction.SatisfiedBy(spell))
            {
                continue;
            }
            var produced = ab.ManaGenerated;
            blocked += color switch
            {
                ValueObjects.ManaColor.White => produced.White,
                ValueObjects.ManaColor.Blue => produced.Blue,
                ValueObjects.ManaColor.Black => produced.Black,
                ValueObjects.ManaColor.Red => produced.Red,
                ValueObjects.ManaColor.Green => produced.Green,
                // CR 107.4c — colorless {C} mana now lives in its own bucket.
                ValueObjects.ManaColor.Colorless => produced.Colorless,
                _ => 0,
            };
        }

        return blocked;
    }
}
