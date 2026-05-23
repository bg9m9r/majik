using Majik.Bot.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Bot.Combat;

public sealed class CombatPolicy
{
    private readonly ArchetypeWeights _weights;
    private readonly int _budgetMs;
    private readonly IBotDecisionSink _sink;

    public CombatPolicy(ArchetypeWeights weights, int budgetMs = 800, IBotDecisionSink? sink = null)
    {
        _weights = weights;
        _budgetMs = budgetMs;
        _sink = sink ?? NullBotDecisionSink.Instance;
    }

    public CombatPlan PickAttackers(GameContext ctx, Player self, IReadOnlyList<Creature> eligible)
    {
        if (eligible.Count == 0) return CombatPlan.None;
        var (plan, _) = CombatSearch.FindBestAttackPlan(ctx, self, eligible, _weights, _budgetMs, _sink);
        return plan;
    }

    /// <summary>
    /// Greedy blocker selection — for each attacker (descending power),
    /// pick the smallest hard-block from <paramref name="eligible"/>.
    /// Trade-blocks are NOT taken in v1 (the heuristic above only assigns
    /// when blocker.Toughness > attacker.Power). Alternatives surfaced
    /// to the sink are constructed by enumerating "what if we'd picked
    /// a different blocker for this attacker" so a human reader can see
    /// the candidate set the bot weighed.
    /// </summary>
    public BlockPlan PickBlockers(
        GameContext ctx, Player self,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligible)
    {
        if (attackers.Count == 0 || eligible.Count == 0) return BlockPlan.None;
        var blockers = new List<BlockerDeclaration>();
        var avail = new List<Creature>(eligible);
        foreach (var att in attackers.OrderByDescending(c => c.Power))
        {
            var blocker = avail.Where(b => b.Toughness > att.Power)
                               .OrderByDescending(b => b.Power).FirstOrDefault();
            if (blocker == null) continue;
            blockers.Add(new BlockerDeclaration(blocker, att));
            avail.Remove(blocker);
        }

        var plan = new BlockPlan(blockers);
        EmitDecision(ctx, self, attackers, eligible, plan);
        return plan;
    }

    private void EmitDecision(
        GameContext ctx, Player self,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> eligible,
        BlockPlan chosen)
    {
        if (ReferenceEquals(_sink, NullBotDecisionSink.Instance)) return;

        var chosenLabel = LabelFor(chosen);
        var chosenScore = ScoreBlockPlan(chosen);

        // Alternatives: for each attacker that received a block, swap in
        // the next-best legal blocker (if any) to surface the runner-up
        // pairing. Also include "no block at all" as a baseline.
        var alts = new List<BotDecisionAlternative>();
        var usedAlt = new HashSet<string>();

        foreach (var decl in chosen.Blockers)
        {
            var alternativeBlocker = eligible
                .Where(b => !ReferenceEquals(b, decl.Blocker)
                    && b.Toughness > decl.Attacker.Power
                    && !chosen.Blockers.Any(d => ReferenceEquals(d.Blocker, b)))
                .OrderByDescending(b => b.Power)
                .FirstOrDefault();
            if (alternativeBlocker != null)
            {
                var label = $"Swap:{decl.Attacker.Name}<-{alternativeBlocker.Name}";
                if (usedAlt.Add(label))
                {
                    alts.Add(new BotDecisionAlternative(
                        label,
                        chosenScore - 0.5));   // rough proxy: runner-up by power
                }
            }
        }

        // Always offer "no block" as a baseline alternative so the reader
        // sees what we'd have absorbed by letting damage through.
        var noBlockLabel = "Block:{}";
        if (chosenLabel != noBlockLabel && usedAlt.Add(noBlockLabel))
        {
            alts.Add(new BotDecisionAlternative(
                noBlockLabel,
                ScoreBlockPlan(BlockPlan.None)));
        }
        alts = alts.OrderByDescending(a => a.Score).Take(3).ToList();

        var ctxFlags = new Dictionary<string, string>
        {
            ["turn"] = ctx.TurnNumber.ToString(),
            ["phase"] = ctx.CurrentPhase?.ToString() ?? "null",
            ["selfLife"] = self.LifeTotal.ToString(),
            ["attackerCount"] = attackers.Count.ToString(),
            ["eligibleBlockers"] = eligible.Count.ToString(),
            ["blocksAssigned"] = chosen.Blockers.Count.ToString(),
        };
        var unblockedPower = attackers.Sum(a => a.Power)
            - chosen.Blockers.Sum(b => b.Attacker.Power);
        if (unblockedPower >= self.LifeTotal) ctxFlags["lethalIncoming"] = "true";
        if (chosen.Blockers.Count == 0) ctxFlags["takeFullDamage"] = "true";

        try
        {
            _sink.Record(new BotDecision(
                DecisionType: "Combat.Blockers",
                Chosen: chosenLabel,
                ChosenScore: chosenScore,
                Alternatives: alts,
                Context: ctxFlags));
        }
        catch { /* observer fault must not abort engine */ }
    }

    /// <summary>
    /// Rough plan score — higher is better for the blocking player.
    /// Sums blocked-attacker power (damage prevented) minus a fractional
    /// penalty for trades. Used only to give the sink a comparable scalar
    /// across the chosen plan and its alternatives — not authoritative
    /// game-state EV.
    /// </summary>
    private static double ScoreBlockPlan(BlockPlan plan)
    {
        double prevented = plan.Blockers.Sum(d => (double)d.Attacker.Power);
        // Trade penalty: blocker dies (blocker.Toughness <= attacker.Power).
        double tradePenalty = plan.Blockers
            .Where(d => d.Blocker.Toughness <= d.Attacker.Power)
            .Sum(d => (double)d.Blocker.Toughness * 0.5);
        return prevented - tradePenalty;
    }

    private static string LabelFor(BlockPlan plan)
    {
        if (plan.Blockers.Count == 0) return "Block:{}";
        var parts = plan.Blockers.Select(d => $"{d.Blocker.Name}->{d.Attacker.Name}");
        return $"Block:{{{string.Join(",", parts)}}}";
    }
}
