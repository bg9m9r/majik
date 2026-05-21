using System.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Bot.Combat;

/// <summary>
/// Depth-limited minimax over attacker subsets. Opponent's block is
/// projected pessimistically. Stopwatch-bounded.
/// </summary>
internal static class CombatSearch
{
    private const int TopKAttackers = 8;

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

        CombatPlan best = CombatPlan.None;
        double bestScore = ScoreFor(Array.Empty<Creature>(), oppBlockers, weights);

        var n = usable.Count;
        for (long mask = 1; mask < (1L << n); mask++)
        {
            if (sw.ElapsedMilliseconds > budgetMs) break;
            var subset = new List<Creature>();
            for (int i = 0; i < n; i++)
                if ((mask & (1L << i)) != 0) subset.Add(usable[i]);

            var score = ScoreFor(subset, oppBlockers, weights);
            if (score > bestScore)
            {
                bestScore = score;
                best = new CombatPlan(
                    subset.Select(c => new AttackerDeclaration(c, opp)).ToList());
            }
        }
        return (best, bestScore);
    }

    private static double ScoreFor(
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> oppBlockers,
        ArchetypeWeights weights)
    {
        var (botLifeLost, oppLifeLost, botPowerKilled, oppPowerKilled)
            = ProjectCombat(attackers, oppBlockers);
        return CombatEval.Score(botLifeLost, oppLifeLost, botPowerKilled, oppPowerKilled, weights);
    }

    private static (int botLifeLost, int oppLifeLost, int botPowerKilled, int oppPowerKilled)
        ProjectCombat(IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> oppBlockers)
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
}
