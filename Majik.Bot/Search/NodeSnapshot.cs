using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Bot.Search;

/// <summary>
/// Resume context for a cached tree-node state (tree-state reuse, Task 2):
/// everything <see cref="EngineSimulator.AdvanceFrom"/> needs — beyond the
/// frozen players themselves — to rebuild a sandbox at the node's position.
///
/// <para>
/// <paramref name="TurnNumber"/> / <paramref name="Phase"/> /
/// <paramref name="ActivePlayerId"/> are tracked during the drive via the bus
/// (<c>TurnStartedEvent</c> / <c>PhaseStateChangedEvent</c> — the spike's
/// <c>AdvanceWithSandbox</c> observer seam). <paramref name="LandDropsUsed"/>
/// carries the per-seat (by <see cref="Player.Id"/>) land drops consumed in
/// the snapshot's turn — the tally lives on the driver, not the players, so a
/// players-only snapshot would otherwise forget it and re-offer the drop
/// (spike BREAK 2); it seeds the restored sandbox's
/// <c>LandDropTracker</c>. <paramref name="SuffixFromParent"/> records the
/// move suffix that was replayed from the previous cached position to reach
/// this node — bookkeeping for the Task 3 nearest-cached-ancestor descent
/// (restoring THIS snapshot itself needs an EMPTY suffix: the frozen players
/// already sit at the decision point).
/// </para>
/// </summary>
internal sealed record ResumeCtx(
    int TurnNumber,
    PhaseStateType Phase,
    Guid ActivePlayerId,
    IReadOnlyList<SimMove> SuffixFromParent,
    IReadOnlyDictionary<Guid, int> LandDropsUsed,
    Majik.Core.Combat.CombatResumeState? PreDeclaredAttack = null)
{
    private static readonly IReadOnlyDictionary<Guid, int> NoDrops =
        new Dictionary<Guid, int>();

    /// <summary>
    /// The resume context of a search ROOT: the root state's own turn / phase /
    /// active seat, an empty suffix, and no consumed land drops (a root capture
    /// happens at a live decision point whose turn-tally the live engine still
    /// owns — sims have always resumed roots with a fresh tracker).
    /// Preserves the root's <see cref="SimState.PreDeclaredAttack"/> so the
    /// reuse path's root-cache resume (AdvanceFrom/RolloutFrom via
    /// <c>StateFromCache</c>) replays a block-search root into the SAME
    /// past-declaration combat the direct Advance path enters. Child-node
    /// snapshots keep the default null: mid-combat positions are never
    /// cache-eligible (<see cref="SnapshotPolicy"/>), so every cached child
    /// sits after the resumed combat completed.
    /// </summary>
    public static ResumeCtx ForRoot(SimState root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new ResumeCtx(
            root.TurnNumber, root.Phase, root.ActivePlayer.Id,
            SuffixFromParent: Array.Empty<SimMove>(),
            LandDropsUsed: NoDrops,
            PreDeclaredAttack: root.PreDeclaredAttack);
    }
}

/// <summary>
/// A cached tree-node state: the frozen (defensively cloned) players at the
/// node's decision point + the <see cref="ResumeCtx"/> to rebuild a sandbox
/// there. Produced by <see cref="EngineSimulator.AdvanceFrom"/> only for
/// CACHE-ELIGIBLE positions (see <see cref="SnapshotPolicy"/>); ineligible /
/// terminal positions yield no snapshot and later restores fall back to the
/// nearest cached ancestor (Task 3).
///
/// <para>Per-search lifetime; never shared across nodes. The players list is a
/// private clone — later engine activity can never mutate it.</para>
/// </summary>
internal sealed record NodeSnapshot(
    IReadOnlyList<Player> Players,
    ResumeCtx Ctx,
    bool IsCacheEligible);

/// <summary>
/// The cache-eligibility predicate, per the spike's caching-policy findings.
/// A node state may be snapshot-cached only when NO engine state lives outside
/// the players:
/// <list type="bullet">
///   <item>BREAK 1 — the Stack subsystem: a spell ON the stack at snapshot
///     time is lost (the Spell object lives on the sandbox Stack; its card
///     orphans in the stack ZONE). Only empty-stack positions are eligible —
///     CR 500.2 makes them plentiful.</item>
///   <item>BREAK 3 — mid-combat sub-state: attack declarations live in
///     CombatFlow (the attacker is already tapped in the snapshot, CR 508.1f),
///     so any decision INSIDE combat after attackers are declared cannot
///     restore. DeclareBlockers nodes and mid-combat priority windows are
///     ineligible; the DeclareAttackers ask itself IS eligible (resuming at
///     the Combat phase start re-reaches it — spike-proven faithful).</item>
/// </list>
/// </summary>
internal static class SnapshotPolicy
{
    /// <summary>
    /// True when a position (players at a reached decision, in the given
    /// phase) may be snapshot-cached. Terminal decisions are never eligible
    /// (there is nothing to resume).
    /// </summary>
    public static bool IsCacheEligible(
        IReadOnlyList<Player> players,
        SimDecision decision,
        PhaseStateType phase)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.IsTerminal)
            return false;

        // BREAK 1 — the stack must be empty across ALL players. The stack
        // ZONE mirrors the Stack subsystem's contents per controller; any
        // card there means an unresolvable orphan after restore.
        foreach (var p in players)
        {
            if (p.Zones.GetZone(ZoneType.Stack).GetCards().Any())
                return false;
        }

        // BREAK 3 — never cache mid-combat sub-state. DeclareBlockers sits
        // after the opponent's attack declaration by definition; any other
        // non-DeclareAttackers decision inside the Combat phase (e.g. a
        // priority window with an instant castable mid-combat) is equally
        // unrestorable because the declaration lives in CombatFlow.
        if (decision.Kind == SimDecisionKind.DeclareBlockers)
            return false;
        if (phase == PhaseStateType.Combat
            && decision.Kind != SimDecisionKind.DeclareAttackers)
        {
            return false;
        }

        return true;
    }
}
