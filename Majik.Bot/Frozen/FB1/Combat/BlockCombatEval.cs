using Majik.Core.Cards;
using Majik.Core.Players.Agents;
// Aliased: importing all of Majik.Core.Combat would make BlockerDeclaration
// ambiguous with Majik.Core.Players.Agents.BlockerDeclaration (the bot's type).
using BlockLegality = Majik.Core.Combat.BlockLegality;

namespace Majik.Bot.Frozen.FB1;

/// <summary>
/// Direct combat-outcome evaluator for block plan selection.
///
/// <para>
/// MCTS cannot reach a DeclareBlockers decision in the sandbox because the
/// opponent agent (DeterministicBotAgent) never declares attackers, so the
/// engine never surfaces a block prompt for the searched seat. Instead,
/// <see cref="SearchStrategy.PickBlockers"/> uses this evaluator to score
/// the enriched candidate set produced by
/// <see cref="Majik.Bot.Search.SearchAgent"/> (chump, trade, gang blocks)
/// and return the plan with the highest projected score from the defender's
/// perspective.
/// </para>
///
/// <para>
/// Block plans are enumerated by <see cref="EnumeratePlans"/> using the same
/// cap logic as the SearchAgent enumerator (MaxBlockMoves = 50):
/// no-block + all 1-to-1 assignments (including chump and trade) +
/// bounded gang-block pairs. <see cref="PickBest"/> scores each plan with a
/// lethal-aware heuristic and returns the best.
/// </para>
/// </summary>
internal static class BlockCombatEval
{
    /// <summary>
    /// Maximum block plan candidates to enumerate. Mirrors the cap in
    /// <see cref="Majik.Bot.Search.SearchAgent"/> so the two enumerations
    /// are in sync. 50 covers all common boards without exponential blow-up.
    /// </summary>
    private const int MaxBlockPlans = 50;

    /// <summary>
    /// Enumerate a bounded, diverse set of legal <see cref="BlockPlan"/>
    /// candidates (same logic as <c>SearchAgent.BuildBlockerMoves</c>):
    ///
    /// <list type="number">
    ///   <item>No-block (empty plan, always first).</item>
    ///   <item>
    ///     Every 1-to-1 assignment (each eligible blocker × each attacker),
    ///     including chump blocks and trades (no survive-only filter).
    ///   </item>
    ///   <item>
    ///     2-blocker gang assignments (two blockers stacked on one attacker),
    ///     bounded per attacker.
    ///   </item>
    /// </list>
    ///
    /// Cap: <see cref="MaxBlockPlans"/>. Enumeration stops once the cap is
    /// reached. De-duplication is by structural equality of the plan key
    /// (blocker-name→attacker-name pairs), preventing duplicate candidates.
    /// </summary>
    public static IReadOnlyList<BlockPlan> EnumeratePlans(
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligibleBlockers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var plans = new List<BlockPlan>();

        void TryAdd(BlockPlan plan)
        {
            var key = PlanKey(plan);
            if (plans.Count < MaxBlockPlans && seen.Add(key))
                plans.Add(plan);
        }

        // Always include no-block.
        TryAdd(BlockPlan.None);

        if (attackers.Count == 0 || eligibleBlockers.Count == 0)
            return plans;

        // 1-to-1: every blocker on every attacker (chump, trade, hard-block — all included).
        foreach (var att in attackers)
        {
            foreach (var blk in eligibleBlockers)
            {
                // CR 509.1b — only legal (blocker, attacker) pairs may block.
                if (!BlockLegality.CanBlock(blk, att, out _)) continue;
                TryAdd(new BlockPlan(new[] { new BlockerDeclaration(blk, att) }));
                if (plans.Count >= MaxBlockPlans) goto done;
            }
        }

        // 2-blocker gang: pairs of blockers stacked on one attacker.
        foreach (var att in attackers)
        {
            for (int i = 0; i < eligibleBlockers.Count; i++)
            {
                // CR 509.1b — both gang members must be legal against att.
                if (!BlockLegality.CanBlock(eligibleBlockers[i], att, out _)) continue;
                for (int j = i + 1; j < eligibleBlockers.Count; j++)
                {
                    if (!BlockLegality.CanBlock(eligibleBlockers[j], att, out _)) continue;
                    TryAdd(new BlockPlan(new[]
                    {
                        new BlockerDeclaration(eligibleBlockers[i], att),
                        new BlockerDeclaration(eligibleBlockers[j], att),
                    }));
                    if (plans.Count >= MaxBlockPlans) goto done;
                }
            }
        }

        done:
        return plans;
    }

    /// <summary>
    /// Score each candidate plan via <see cref="ScorePlan"/> and return the
    /// plan with the highest score. The score is from the DEFENDER's perspective
    /// (higher = better for the blocker). The lethal-aware check ensures that a
    /// plan that lets the defender die scores <c>double.MinValue</c>, so any
    /// survival block beats taking lethal damage — even a chump block that
    /// sacrifices the blocker.
    /// </summary>
    public static BlockPlan PickBest(
        IReadOnlyList<BlockPlan> candidates,
        IReadOnlyList<Creature> attackers,
        int defenderLife,
        ArchetypeWeights weights)
    {
        if (candidates.Count == 0)
            return BlockPlan.None;

        var best = candidates[0];
        var bestScore = ScorePlan(best, attackers, defenderLife, weights);

        for (int i = 1; i < candidates.Count; i++)
        {
            var score = ScorePlan(candidates[i], attackers, defenderLife, weights);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidates[i];
            }
        }

        return best;
    }

    /// <summary>
    /// Score a <see cref="BlockPlan"/> from the DEFENDER's perspective.
    ///
    /// <para>
    /// Lethal check: if unblocked attacker power ≥ defender's current life, the
    /// plan is lethal and scores <c>double.MinValue</c>. This ensures any
    /// survival block (including a chump that sacrifices the blocker) beats
    /// taking fatal damage.
    /// </para>
    ///
    /// <para>
    /// Components:
    /// <list type="bullet">
    ///   <item>Life saved (damage prevented by blocks) × LifeDelta weight.</item>
    ///   <item>
    ///     Attacker power killed (attacker killed by blockers) × BoardPower weight
    ///     — positive (defender's board benefit).
    ///   </item>
    ///   <item>
    ///     Blocker power lost (blocker dies to attacker) × BoardPower weight
    ///     — negative (defender's board cost).
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    private static double ScorePlan(
        BlockPlan plan,
        IReadOnlyList<Creature> allAttackers,
        int defenderLife,
        ArchetypeWeights weights)
    {
        // Group blockers by attacker InstanceId for gang-block handling.
        var blockersByAttackerId = plan.Blockers
            .GroupBy(d => d.Attacker.InstanceId)
            .ToDictionary(g => g.Key, g => g.Select(d => d.Blocker).ToList());

        int unblockedDamage = 0;
        int attackersKilledPower = 0; // sum of P+T of attackers killed
        int blockersKilledPower = 0;  // sum of P+T of blockers killed

        foreach (var att in allAttackers)
        {
            if (!blockersByAttackerId.TryGetValue(att.InstanceId, out var stackedBlockers)
                || stackedBlockers.Count == 0)
            {
                unblockedDamage += att.Power;
                continue;
            }

            // Sum blocker power to check if attacker dies.
            int totalBlockerPower = stackedBlockers.Sum(b => b.GetEffectivePower());
            bool attackerDies = totalBlockerPower >= att.Toughness;
            if (attackerDies)
                attackersKilledPower += att.Power + att.Toughness;

            // Attacker's power is distributed across blockers (optimal for
            // attacker = maximise kills). Use ascending toughness ordering.
            int remaining = att.Power;
            foreach (var blk in stackedBlockers.OrderBy(b => b.GetEffectiveToughness()))
            {
                if (remaining >= blk.GetEffectiveToughness())
                {
                    blockersKilledPower += blk.GetEffectivePower() + blk.GetEffectiveToughness();
                    remaining -= blk.GetEffectiveToughness();
                }
            }
        }

        // Lethal-aware: plan is fatal → score as worst possible.
        if (unblockedDamage >= defenderLife)
            return double.MinValue;

        // Score from defender's POV: life saved - board cost + board gain.
        // "Life saved" = damage prevented vs no-block baseline.
        int totalAttackPower = allAttackers.Sum(a => a.Power);
        int damagePrevented = totalAttackPower - unblockedDamage;

        // Near-lethal scaling: the value of preventing damage scales with how
        // dangerous the incoming attack is relative to current life. At 20 life
        // absorbing 2 damage matters little; at 3 life absorbing 5 damage is
        // nearly-lethal (and actually lethal, caught above). This prevents the
        // evaluator from over-weighting chump blocks in safe life-total situations
        // while still heavily favouring them near death.
        //   scale = clamp(totalAttackPower / defenderLife, 0, 5)
        // Examples: 2 atk / 20 life = 0.1 (low), 5 atk / 3 life ≈ 1.67 (high).
        double threatScale = Math.Clamp((double)totalAttackPower / defenderLife, 0.0, 5.0);

        return weights.LifeDelta * damagePrevented * threatScale
             + weights.BoardPower * attackersKilledPower
             - weights.BoardPower * blockersKilledPower;
    }

    private static string PlanKey(BlockPlan plan)
    {
        if (plan.Blockers.Count == 0) return "Block:{}";
        var parts = plan.Blockers
            .OrderBy(d => d.Blocker.InstanceId)
            .Select(d => $"{d.Blocker.Name}->{d.Attacker.Name}");
        return $"Block:{{{string.Join(",", parts)}}}";
    }
}
