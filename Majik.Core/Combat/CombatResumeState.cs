using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.Combat;

/// <summary>
/// Id-level snapshot of a declared attack, used to resume a SANDBOX clone
/// into combat past the declaration (root block search). Carries no object
/// references — attackers resolve by InstanceId and players by Id against
/// the clone at rebind time, so the record is safe to hand across the
/// live→sandbox boundary.
/// Limitation (documented): when built from the defender's block prompt the
/// per-attacker defending target is unknown (the agent API only passes
/// creatures), so all attackers rebind targeting the defending PLAYER —
/// planeswalker attacks are modeled as player attacks in-sim, matching what
/// the BlockCombatEval path assumes today.
/// </summary>
public sealed record CombatResumeState(
    IReadOnlyList<Guid> AttackerInstanceIds,
    Guid DefendingPlayerId)
{
    public static CombatResumeState FromAttackers(IEnumerable<Creature> attackers, Player defendingPlayer)
        => new(attackers.Select(a => a.InstanceId).ToList(), defendingPlayer.Id);

    /// <summary>Resolve this snapshot against (cloned) players; null when no attacker survives.</summary>
    public CombatPlan? Rebind(IReadOnlyList<Player> players)
    {
        var defender = players.FirstOrDefault(p => p.Id == DefendingPlayerId);
        if (defender is null) return null;
        var byId = players
            .SelectMany(p => p.Zones.Battlefield.GetCards().OfType<Creature>())
            .ToDictionary(c => c.InstanceId);
        var decls = AttackerInstanceIds
            .Where(byId.ContainsKey)
            .Select(id => new Players.Agents.AttackerDeclaration(byId[id], defender))
            .ToList();
        return decls.Count == 0 ? null : new CombatPlan(decls);
    }
}
