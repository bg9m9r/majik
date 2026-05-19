using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;

namespace Majik.Core.Combat;

/// <summary>
/// CR 510 damage assignment + resolution, extracted from
/// <see cref="CombatManager"/>. Handles both the first-strike sub-step
/// and the regular damage step; the coordinator decides when to invoke
/// which.
///
/// Trample distribution (CR 702.19), deathtouch lethal-damage shortcut
/// (CR 702.2), and per-blocker assignment all live here so the combat
/// orchestrator stays focused on step transitions.
/// </summary>
public sealed class CombatDamageAssigner
{
    private readonly IEventBus? _eventBus;

    public CombatDamageAssigner(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>True if any creature in the combat will deal damage during
    /// the first-strike sub-step (CR 702.7 / 702.4).</summary>
    public bool HasFirstStrikeDamage(Combat combat)
        => combat.Attackers.Any(a => a.CanDealFirstStrikeDamage())
        || combat.GetAllBlockers().Any(b => b.CanDealFirstStrikeDamage());

    /// <summary>Assign and resolve a single damage sub-step. Pass
    /// <paramref name="isFirstStrike"/>=true for the 510a sub-step,
    /// false for the regular 510b step.</summary>
    public void AssignAndResolve(Combat combat, bool isFirstStrike)
    {
        if (isFirstStrike)
        {
            foreach (var attacker in combat.Attackers)
            {
                if (attacker.CanDealFirstStrikeDamage()) Assign(attacker);
            }
        }
        else
        {
            foreach (var attacker in combat.Attackers)
            {
                if (attacker.CanDealRegularDamage()) Assign(attacker);
            }
        }

        Resolve(combat, isFirstStrike);
    }

    /// <summary>Clear assigned-damage trackers so the regular-damage step
    /// can repopulate them after first strike resolves.</summary>
    public void Reset(Combat combat)
    {
        foreach (var attacker in combat.Attackers)
        {
            attacker.ResetDamageAssignment();
            foreach (var blocker in attacker.Blockers)
            {
                blocker.ResetDamageAssignment();
            }
        }
    }

    /// <summary>Every creature (attacker + blocker) currently in this
    /// combat. Useful for SBA checks that need the combat-scoped card
    /// universe.</summary>
    public IEnumerable<ICard> GetCombatCreatures(Combat combat)
    {
        foreach (var attacker in combat.Attackers)
        {
            yield return attacker.Creature;
        }
        foreach (var blocker in combat.GetAllBlockers())
        {
            yield return blocker.Creature;
        }
    }

    private void Assign(Attacker attacker)
    {
        int remainingPower = attacker.GetPower();

        if (attacker.Blockers.Count == 0)
        {
            attacker.AssignDamage(remainingPower);
            return;
        }

        foreach (var blocker in attacker.Blockers)
        {
            if (remainingPower <= 0) break;

            int lethalDamage = CalculateLethalDamage(blocker.Creature, attacker.HasDeathtouch);
            int assignedDamage = attacker.HasTrample
                ? Math.Min(lethalDamage, remainingPower)
                : remainingPower;

            blocker.AssignDamage(assignedDamage);
            attacker.AssignDamage(assignedDamage);
            remainingPower -= assignedDamage;
        }

        // CR 702.19b — trampling excess hits target.
        if (attacker.HasTrample && remainingPower > 0)
        {
            attacker.AssignDamage(remainingPower);
        }
    }

    private static int CalculateLethalDamage(Creature creature, bool hasDeathtouch)
        => hasDeathtouch ? 1 : creature.Toughness;

    private void Resolve(Combat combat, bool isFirstStrike)
    {
        foreach (var attacker in combat.Attackers)
        {
            foreach (var blocker in attacker.Blockers)
            {
                if (blocker.AssignedDamage > 0)
                {
                    blocker.Creature.TakeDamage(blocker.AssignedDamage);
                    _eventBus?.Publish(new CombatDamageDealtEvent(
                        attacker.Creature, blocker.Creature, blocker.AssignedDamage, isFirstStrike));
                }
            }

            int targetDamage = attacker.AssignedDamage - attacker.Blockers.Sum(b => b.AssignedDamage);
            if (targetDamage > 0)
            {
                if (attacker.TargetPlayer != null)
                {
                    attacker.TargetPlayer.LoseLife(targetDamage);
                    _eventBus?.Publish(new CombatDamageDealtEvent(
                        attacker.Creature, attacker.TargetPlayer, targetDamage, isFirstStrike));
                }
                else if (attacker.TargetPlaneswalker != null)
                {
                    attacker.TargetPlaneswalker.RemoveLoyalty(targetDamage);
                    _eventBus?.Publish(new CombatDamageDealtEvent(
                        attacker.Creature, attacker.TargetPlaneswalker, targetDamage, isFirstStrike));
                }
            }

            foreach (var blocker in attacker.Blockers)
            {
                if (blocker.Creature.Power > 0)
                {
                    attacker.Creature.TakeDamage(blocker.Creature.Power);
                    _eventBus?.Publish(new CombatDamageDealtEvent(
                        blocker.Creature, attacker.Creature, blocker.Creature.Power, isFirstStrike));
                }
            }
        }
    }
}
