using System.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Bot.Combat;

/// <summary>
/// Depth-limited minimax over attacker subsets. Stopwatch-bounded.
///
/// Two passes (iterative deepening):
///   Pass 1 — for each attacker subset, project opponent blocks greedily
///   (cheap, used for every board size).
///   Pass 2 — if time remains AND the board is small enough that exhaustive
///   opponent block assignment is tractable, redo the search with true
///   minimax over opponent block assignments (pessimistic for bot). This
///   catches cases where the greedy block projection is over-optimistic for
///   the attacker, leading to a false-positive attack.
/// </summary>
internal static class CombatSearch
{
    private const int TopKAttackers = 8;

    // Pass 2 is exponential in (attackers x blockers). Cap both sides at 4
    // so worst case is 2^4 subsets x 5^4 assignments = 10,000 evaluations.
    private const int DeepAttackerCap = 4;
    private const int DeepBlockerCap = 4;

    public static (CombatPlan plan, double score) FindBestAttackPlan(
        GameContext ctx, Player self, IReadOnlyList<Creature> eligible,
        ArchetypeWeights weights, int budgetMs)
    {
        var sw = Stopwatch.StartNew();
        var usable = eligible.Where(c => c.Power > 0).ToList();
        if (usable.Count > TopKAttackers)
            usable = usable.OrderByDescending(c => c.Power).Take(TopKAttackers).ToList();

        var opp = ctx.AllPlayers.First(p => !ReferenceEquals(p, self));
        var oppBlockers = opp.Zones.Battlefield.GetCards().OfType<Creature>().ToList();

        // Pass 1: greedy block projection over all attacker subsets.
        var (best, bestScore) = SearchSubsets(
            usable, oppBlockers, opp, weights, sw, budgetMs,
            (subset, blockers) => ScoreSubsetGreedy(subset, blockers, weights));

        // Pass 2 (iterative deepening): if board is small and we still have
        // time, redo with true opponent-block enumeration. This is strictly
        // more accurate; replace best plan only when the deeper score
        // disagrees.
        if (usable.Count <= DeepAttackerCap
            && oppBlockers.Count <= DeepBlockerCap
            && sw.ElapsedMilliseconds < budgetMs)
        {
            var (deepBest, deepScore) = SearchSubsets(
                usable, oppBlockers, opp, weights, sw, budgetMs,
                (subset, blockers) => ScoreSubsetMinimax(subset, blockers, weights));
            return (deepBest, deepScore);
        }

        return (best, bestScore);
    }

    private static (CombatPlan plan, double score) SearchSubsets(
        IReadOnlyList<Creature> usable,
        IReadOnlyList<Creature> oppBlockers,
        Player opp,
        ArchetypeWeights weights,
        Stopwatch sw, int budgetMs,
        Func<IReadOnlyList<Creature>, IReadOnlyList<Creature>, double> scorer)
    {
        CombatPlan best = CombatPlan.None;
        double bestScore = scorer(Array.Empty<Creature>(), oppBlockers);

        var n = usable.Count;
        for (long mask = 1; mask < (1L << n); mask++)
        {
            if (sw.ElapsedMilliseconds > budgetMs) break;
            var subset = new List<Creature>();
            for (int i = 0; i < n; i++)
                if ((mask & (1L << i)) != 0) subset.Add(usable[i]);

            var score = scorer(subset, oppBlockers);
            if (score > bestScore)
            {
                bestScore = score;
                best = new CombatPlan(
                    subset.Select(c => new AttackerDeclaration(c, opp)).ToList());
            }
        }
        return (best, bestScore);
    }

    private static double ScoreSubsetGreedy(
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> oppBlockers,
        ArchetypeWeights weights)
    {
        var (botLifeLost, oppLifeLost, botPowerKilled, oppPowerKilled)
            = ProjectCombatGreedy(attackers, oppBlockers);
        return CombatEval.Score(botLifeLost, oppLifeLost, botPowerKilled, oppPowerKilled, weights);
    }

    /// <summary>
    /// Enumerate every legal opponent block assignment and return the
    /// score the opponent would pick (minimum from the bot's view).
    /// Each blocker chooses one of: pass, or block attacker i (0..n-1).
    /// </summary>
    private static double ScoreSubsetMinimax(
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> oppBlockers,
        ArchetypeWeights weights)
    {
        if (attackers.Count == 0)
            return CombatEval.Score(0, 0, 0, 0, weights);

        var assignment = new int[oppBlockers.Count]; // -1 = no block
        for (int i = 0; i < assignment.Length; i++) assignment[i] = -1;

        var attackerCount = attackers.Count;
        var blockerCount = oppBlockers.Count;
        double worstForBot = double.PositiveInfinity;
        bool anyEvaluated = false;

        // Each blocker picks among (attackerCount + 1) options:
        // option 0 = no block, options 1..attackerCount = block attacker (option-1).
        // Cap exponent to avoid runaway in pathological inputs.
        long totalCombos = 1;
        for (int i = 0; i < blockerCount; i++)
        {
            totalCombos *= (attackerCount + 1);
            if (totalCombos > 100_000) { totalCombos = -1; break; }
        }

        if (totalCombos < 0)
        {
            // Fall back to greedy if combos blow up (shouldn't happen with caps).
            return ScoreSubsetGreedy(attackers, oppBlockers, weights);
        }

        for (long combo = 0; combo < totalCombos; combo++)
        {
            long c = combo;
            for (int i = 0; i < blockerCount; i++)
            {
                assignment[i] = (int)(c % (attackerCount + 1)) - 1;
                c /= (attackerCount + 1);
            }

            var (botLifeLost, oppLifeLost, botKilled, oppKilled) =
                ProjectCombatWithAssignment(attackers, oppBlockers, assignment);
            var score = CombatEval.Score(botLifeLost, oppLifeLost, botKilled, oppKilled, weights);

            if (!anyEvaluated || score < worstForBot)
            {
                worstForBot = score;
                anyEvaluated = true;
            }
        }
        return worstForBot;
    }

    /// <summary>
    /// Greedy block projection: for each attacker (highest power first),
    /// pick the smallest-acceptable hard-block else a trade-block else
    /// unblocked. Pessimistic-ish but single-pass — does not enumerate
    /// alternative block assignments.
    /// </summary>
    private static (int botLifeLost, int oppLifeLost, int botPowerKilled, int oppPowerKilled)
        ProjectCombatGreedy(IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> oppBlockers)
    {
        var ordered = attackers.OrderByDescending(c => c.Power).ToList();
        var avail = new List<Creature>(oppBlockers);

        int oppLifeLost = 0, botPowerKilled = 0, oppPowerKilled = 0;
        int botLifeLost = 0;

        foreach (var att in ordered)
        {
            var blocker = avail.Where(b => b.Toughness > att.Power)
                               .OrderByDescending(b => b.Power).FirstOrDefault();
            if (blocker == null)
                blocker = avail.Where(b => b.Power >= att.Toughness)
                               .OrderByDescending(b => b.Power).FirstOrDefault();
            if (blocker == null) { oppLifeLost += att.Power; continue; }
            avail.Remove(blocker);
            if (blocker.Power >= att.Toughness) botPowerKilled += att.Power + att.Toughness;
            if (att.Power    >= blocker.Toughness) oppPowerKilled += blocker.Power + blocker.Toughness;
        }
        return (botLifeLost, oppLifeLost, botPowerKilled, oppPowerKilled);
    }

    /// <summary>
    /// Project combat outcome given an explicit block assignment.
    /// assignment[i] = -1 (blocker i passes) or j in [0, attackers.Count)
    /// (blocker i blocks attackers[j]). When multiple blockers stack on one
    /// attacker their power sums for kill determination.
    /// </summary>
    private static (int botLifeLost, int oppLifeLost, int botPowerKilled, int oppPowerKilled)
        ProjectCombatWithAssignment(
            IReadOnlyList<Creature> attackers,
            IReadOnlyList<Creature> oppBlockers,
            int[] assignment)
    {
        int oppLifeLost = 0, botPowerKilled = 0, oppPowerKilled = 0;
        int botLifeLost = 0;

        // For each attacker: collect its blockers, compute damage swap.
        // Attacker assigns its power across its blockers in best-for-bot
        // order (kills as many blockers as possible). Even with non-optimal
        // damage assignment by the bot, in this leaf model we assume the
        // bot allocates optimally — opp can already pick the worst block
        // assignment, so we don't double-penalize.
        for (int ai = 0; ai < attackers.Count; ai++)
        {
            var att = attackers[ai];
            var blockersForThis = new List<Creature>();
            for (int bi = 0; bi < assignment.Length; bi++)
                if (assignment[bi] == ai) blockersForThis.Add(oppBlockers[bi]);

            if (blockersForThis.Count == 0) { oppLifeLost += att.Power; continue; }

            // Sum blocker power → does it kill attacker?
            var totalBlockerPower = blockersForThis.Sum(b => b.Power);
            if (totalBlockerPower >= att.Toughness)
                botPowerKilled += att.Power + att.Toughness;

            // Bot allocates att.Power across blockers; pick blockers in
            // ascending toughness order, killing as many as possible.
            int remaining = att.Power;
            foreach (var blk in blockersForThis.OrderBy(b => b.Toughness))
            {
                if (remaining >= blk.Toughness)
                {
                    oppPowerKilled += blk.Power + blk.Toughness;
                    remaining -= blk.Toughness;
                }
                else break;
            }
        }
        return (botLifeLost, oppLifeLost, botPowerKilled, oppPowerKilled);
    }
}
