using Majik.Core.Cards;

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
        reason = string.Empty;
        return true;
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
}
