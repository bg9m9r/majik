using Majik.Core.Cards;

namespace Majik.Bot.Frozen.FB1;

/// <summary>
/// Per-decision memo over <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/>.
/// The minimax pass re-queries the same (blocker, attacker) pairs across
/// thousands of assignments; CanBlock does string-based ability lookups, so
/// cache by InstanceId pair. Scope one instance per FindBestAttackPlan call —
/// legality is stable within a single decision.
/// </summary>
internal sealed class BlockLegalityMemo
{
    private readonly Dictionary<(Guid blocker, Guid attacker), bool> _memo = new();

    public bool CanBlock(Creature blocker, Creature attacker)
    {
        var key = (blocker.InstanceId, attacker.InstanceId);
        if (!_memo.TryGetValue(key, out var ok))
        {
            ok = Majik.Core.Combat.BlockLegality.CanBlock(blocker, attacker, out _);
            _memo[key] = ok;
        }
        return ok;
    }
}
