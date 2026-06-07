using Majik.Core.Players.Agents;

namespace Majik.Bot.Search;

/// <summary>
/// One legal move at a decision point in a simulated game. Discriminated over
/// the three combat/priority decision shapes the search harness intercepts.
/// </summary>
public sealed class SimMove
{
    /// <summary>Stable edge label for MCTS tree keying.</summary>
    public string Key { get; }

    /// <summary>Non-null when this is a DeclareAttackers move.</summary>
    public CombatPlan? CombatPlan { get; }

    /// <summary>Non-null when this is a DeclareBlockers move.</summary>
    public BlockPlan? BlockPlan { get; }

    /// <summary>Non-null when this is a Priority move.</summary>
    public PriorityAction? PriorityAction { get; }

    /// <summary>True when this move declares no attackers (pass-attack).</summary>
    public bool IsEmptyAttack => CombatPlan is { } p && p.Attackers.Count == 0;

    /// <summary>
    /// True when this move declares the maximum number of eligible attackers
    /// (every eligible attacker attacks). Populated for attacker-subset moves.
    /// </summary>
    public bool IsAllOutAttack { get; }

    /// <summary>True when this is a priority-pass move.</summary>
    public bool IsPass => PriorityAction is PriorityAction.PassAction;

    private SimMove(string key, CombatPlan plan, bool isAllOut)
    {
        Key = key;
        CombatPlan = plan;
        IsAllOutAttack = isAllOut;
    }

    private SimMove(string key, BlockPlan plan)
    {
        Key = key;
        BlockPlan = plan;
    }

    private SimMove(string key, PriorityAction action)
    {
        Key = key;
        PriorityAction = action;
    }

    // ── Factory helpers ──────────────────────────────────────────────────────

    internal static SimMove FromCombatPlan(CombatPlan plan, int eligibleCount)
    {
        var key = plan.Attackers.Count == 0
            ? "Attack:{}"
            : $"Attack:{{{string.Join(",", plan.Attackers.Select(a => a.Attacker.Name))}}}";
        var isAllOut = plan.Attackers.Count == eligibleCount && eligibleCount > 0;
        return new SimMove(key, plan, isAllOut);
    }

    internal static SimMove FromBlockPlan(BlockPlan plan)
    {
        var key = plan.Blockers.Count == 0
            ? "Block:{}"
            : $"Block:{{{string.Join(",", plan.Blockers.Select(b => $"{b.Blocker.Name}->{b.Attacker.Name}"))}}}";
        return new SimMove(key, plan);
    }

    internal static SimMove FromPriorityAction(PriorityAction action)
    {
        var key = action switch
        {
            PriorityAction.PassAction => "Pass",
            PriorityAction.CastSpell cs => $"Cast:{cs.Card.Name}",
            PriorityAction.PlayLand pl => $"Land:{pl.Land.Name}",
            PriorityAction.ActivateAbility aa => $"Activate:{aa.Ability}",
            _ => action.ToString() ?? "Unknown"
        };
        return new SimMove(key, action);
    }
}
