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

    public CombatPolicy(ArchetypeWeights weights, int budgetMs = 800)
    {
        _weights = weights;
        _budgetMs = budgetMs;
    }

    public CombatPlan PickAttackers(GameContext ctx, Player self, IReadOnlyList<Creature> eligible)
    {
        if (eligible.Count == 0) return CombatPlan.None;
        var (plan, _) = CombatSearch.FindBestAttackPlan(ctx, self, eligible, _weights, _budgetMs);
        return plan;
    }

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
        return new BlockPlan(blockers);
    }
}
