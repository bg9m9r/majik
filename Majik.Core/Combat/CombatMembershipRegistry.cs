using Majik.Core.Cards;

namespace Majik.Core.Combat;

/// <summary>
/// Live "who is attacking / blocking right now" membership for the current
/// combat (CR 508 / CR 509). The live engine drives combat through
/// <see cref="CombatFlow"/>, which holds its attacker/blocker plan only in
/// method-local <c>CombatPlan</c> objects — there was previously no per-game,
/// queryable combat-membership surface an in-response activated ability could
/// consult mid-combat. (<see cref="CombatManager.CurrentCombat"/> is a parallel
/// model the live <c>TurnDriver</c>/<see cref="CombatFlow"/> path does NOT
/// populate.)
///
/// <para>This registry is that surface. <see cref="CombatFlow"/> records each
/// declared attacker (right after CR 508.1 declaration, before the
/// declare-attackers priority window) and each declared blocker (right after
/// CR 509.1 declaration, before the declare-blockers priority window), and
/// <see cref="Clear"/>s the set when combat ends. Because the records are
/// installed BEFORE the combat priority windows run, an ability activated in
/// response during combat (e.g. Eiganjo, Seat of the Empire's channel —
/// "deal 4 damage to target attacking or blocking creature", CR 702.74) reads
/// a faithful live membership at both candidate-gathering and resolution.</para>
///
/// <para>It is a per-game ambient registry, installed by
/// <see cref="Majik.Core.Game.GameRegistryScope.PushForGame"/> via
/// <see cref="CombatMembershipRegistryProvider"/> and mirrored on the
/// <see cref="AttackRestrictionRegistry"/> / <c>AdditionalCombatQueue</c>
/// pattern: the orchestrator installs one instance, <see cref="CombatFlow"/>
/// writes to it, and effect closures read it via the provider's
/// <c>Current</c>. Outside a game scope (most unit tests) the provider resolves
/// a process-wide fallback, so direct callers work unchanged.</para>
/// </summary>
public sealed class CombatMembershipRegistry
{
    private readonly object _lock = new();
    private readonly HashSet<Creature> _attacking = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Creature> _blocking = new(ReferenceEqualityComparer.Instance);

    /// <summary>Mark <paramref name="creature"/> as a declared attacker
    /// (CR 508.1) in the current combat.</summary>
    public void RecordAttacker(Creature creature)
    {
        if (creature == null) return;
        lock (_lock) { _attacking.Add(creature); }
    }

    /// <summary>Mark <paramref name="creature"/> as a declared blocker
    /// (CR 509.1) in the current combat.</summary>
    public void RecordBlocker(Creature creature)
    {
        if (creature == null) return;
        lock (_lock) { _blocking.Add(creature); }
    }

    /// <summary>Drop all membership — called when a combat ends (CR 511.3) so
    /// the set never leaks an attacker/blocker into a later combat or the
    /// post-combat main phase.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _attacking.Clear();
            _blocking.Clear();
        }
    }

    /// <summary>True iff <paramref name="creature"/> is a declared attacker in
    /// the current combat (CR 508.4 — "attacking" until removed from combat /
    /// combat ends).</summary>
    public bool IsAttacking(Creature creature)
    {
        if (creature == null) return false;
        lock (_lock) { return _attacking.Contains(creature); }
    }

    /// <summary>True iff <paramref name="creature"/> is a declared blocker in
    /// the current combat (CR 509.1 — "blocking" until removed from combat /
    /// combat ends).</summary>
    public bool IsBlocking(Creature creature)
    {
        if (creature == null) return false;
        lock (_lock) { return _blocking.Contains(creature); }
    }

    /// <summary>True iff <paramref name="creature"/> is attacking OR blocking in
    /// the current combat — the Eiganjo / Desert combat-state target gate.</summary>
    public bool IsAttackingOrBlocking(Creature creature)
        => IsAttacking(creature) || IsBlocking(creature);

    /// <summary>Snapshot of the creatures currently attacking or blocking
    /// (de-duplicated by reference). Used by candidate gatherers to offer only
    /// legal targets (CR 601.2c).</summary>
    public IReadOnlyList<Creature> AttackingOrBlocking()
    {
        lock (_lock)
        {
            var result = new List<Creature>(_attacking.Count + _blocking.Count);
            foreach (var c in _attacking) result.Add(c);
            foreach (var c in _blocking)
            {
                if (!_attacking.Contains(c)) result.Add(c);
            }
            return result;
        }
    }
}
