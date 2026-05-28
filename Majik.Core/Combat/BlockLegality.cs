using Majik.Core.Cards;
using Majik.Core.Effects;

namespace Majik.Core.Combat;

/// <summary>
/// Pure functions answering "is this attack / block declaration legal?"
/// per CR 508/509 + keyword interactions. CombatFlow consults these
/// before accepting a CombatPlan / BlockPlan.
/// </summary>
public static class BlockLegality
{
    /// <summary>
    /// CR 508.1a — a creature can attack unless it has defender or
    /// summoning sickness (without haste) or is tapped.
    /// </summary>
    public static bool CanAttack(Creature creature, out string reason)
    {
        if (CombatAbilities.HasDefender(creature))
        {
            reason = "creature has defender";
            return false;
        }
        if (creature.IsTapped)
        {
            reason = "creature is tapped";
            return false;
        }
        if (creature.HasSummoningSickness && !CombatAbilities.HasHaste(creature))
        {
            reason = "creature has summoning sickness";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// CR 509.1b — a creature can block unless it is tapped, and any
    /// "can't be blocked by" / "can only be blocked by" restrictions on
    /// the attacker are satisfied. Currently handled: Flying.
    /// </summary>
    public static bool CanBlock(Creature blocker, Creature attacker, out string reason)
    {
        if (blocker.IsTapped)
        {
            reason = "blocker is tapped";
            return false;
        }
        if (CombatAbilities.HasFlying(attacker) && !CombatAbilities.CanBlockFlying(blocker))
        {
            reason = "attacker has flying; blocker lacks flying or reach";
            return false;
        }

        // CR 509.1b — any "can't be blocked except by …" restriction on the
        // attacker must be satisfied. Restrictions intersect: a would-be
        // blocker must satisfy EVERY active CantBeBlockedExceptByEffect
        // attached to this attacker. The effect is queried via the
        // attacker's ActiveEffects service (CR 613); attackers without an
        // attached service simply have no such restrictions.
        if (!CantBeBlockedExceptBySatisfied(attacker, blocker))
        {
            reason = "attacker has \"can't be blocked except by …\" restriction not satisfied";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// CR 509.1b — walks the attacker's <see cref="Creature.ActiveEffects"/>
    /// for every active <see cref="CantBeBlockedExceptByEffect"/> and returns
    /// true iff EVERY predicate accepts <paramref name="blocker"/>. Multiple
    /// restrictions intersect — any single one rejecting the blocker forbids
    /// the block.
    /// </summary>
    public static bool CantBeBlockedExceptBySatisfied(Creature attacker, Creature blocker)
    {
        var svc = attacker.ActiveEffects;
        if (svc == null) return true;
        return svc.CanBlockUnderExceptByRestrictions(attacker, blocker);
    }

    /// <summary>
    /// CR 702.110a — menace: can't be blocked except by two or more creatures.
    /// Returns true iff this attacker's menace restriction is satisfied by
    /// the declared blocker count (or it has no menace).
    /// </summary>
    public static bool MenaceSatisfied(Creature attacker, int blockerCount)
    {
        if (!CombatAbilities.HasMenace(attacker)) return true;
        if (blockerCount == 0) return true; // unblocked is fine; menace only restricts who CAN block
        return blockerCount >= 2;
    }

    /// <summary>
    /// CR 509.1b — parameterised "can't be blocked except by N or more
    /// creatures" restriction (e.g. Troll of Khazad-dûm requires N≥3).
    /// Returns true iff no such restriction exists on the attacker, or the
    /// <paramref name="blockerCount"/> is zero (going unblocked is always
    /// legal — the restriction only governs who may participate in a block),
    /// or <paramref name="blockerCount"/> ≥ N.
    /// </summary>
    public static bool MinBlockersSatisfied(Creature attacker, int blockerCount)
    {
        var n = CombatAbilities.GetMinBlockerRestriction(attacker);
        if (n == null) return true;
        if (blockerCount == 0) return true; // unblocked is always legal
        return blockerCount >= n.Value;
    }
}
