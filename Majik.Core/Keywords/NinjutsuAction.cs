using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.49 — Ninjutsu [cost]: "[cost], Return an unblocked attacker you
/// control to hand: Put this card onto the battlefield from your hand tapped
/// and attacking."
///
/// Ninjutsu is a special action available only during the declare-blockers
/// step, after blockers are declared, while the Ninja card is in its owner's
/// hand (CR 702.49a). Paying it:
///   1. Returns an UNBLOCKED attacker the player controls to hand
///      (CR 702.49e — the returned creature leaves combat, CR 506.4).
///   2. Puts the Ninja onto the battlefield from hand tapped and attacking
///      the same defender the returned attacker was attacking (CR 702.49b/d).
///
/// This is a reusable primitive — several Modern cards carry Ninjutsu (Ninja
/// of the Deep Hours, Yuriko, Kaito Bane of Nightmares, …). The mana portion
/// of the cost is the caller's responsibility (charged before
/// <see cref="Execute"/>); this primitive performs the return-attacker +
/// enter-tapped-and-attacking swap.
/// </summary>
public static class NinjutsuAction
{
    /// <summary>
    /// CR 702.49a — Ninjutsu may be used only when an unblocked attacker the
    /// caster controls exists in the current combat AND the Ninja card is in
    /// the caster's hand. (The mana portion's affordability is checked by the
    /// caller.)
    /// </summary>
    public static bool CanExecute(ICard ninja, Player caster, CombatManager combat)
    {
        if (ninja == null || caster == null || combat == null) return false;
        if (ninja.Zone != ZoneType.Hand) return false;
        if (!ReferenceEquals(ninja.Owner, caster)) return false;
        return FindUnblockedAttacker(caster, combat) != null;
    }

    /// <summary>
    /// CR 702.49b/e — return an unblocked attacker <paramref name="caster"/>
    /// controls to hand, then put <paramref name="ninja"/> onto the battlefield
    /// tapped and attacking the same defender.
    ///
    /// When <paramref name="returnedAttacker"/> is supplied it is used (the
    /// caller chose which unblocked attacker to bounce); otherwise the first
    /// unblocked attacker the caster controls is auto-picked. Routes the
    /// returned-attacker bounce + the ninja's enter through
    /// <paramref name="zoneService"/> when supplied so leaves/enters events
    /// fire (CR 603.6a); falls back to raw zone moves otherwise.
    ///
    /// Returns the <see cref="Attacker"/> entry the ninja joined combat as, or
    /// <c>null</c> when the action could not be performed (no combat / no
    /// unblocked attacker / ninja not in hand).
    /// </summary>
    public static Attacker? Execute(
        Creature ninja,
        Player caster,
        CombatManager combat,
        ZoneService? zoneService = null,
        Creature? returnedAttacker = null)
    {
        if (ninja == null) throw new ArgumentNullException(nameof(ninja));
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (combat == null) throw new ArgumentNullException(nameof(combat));

        if (ninja.Zone != ZoneType.Hand) return null;
        if (!ReferenceEquals(ninja.Owner, caster)) return null;
        if (combat.CurrentCombat == null || combat.CurrentCombat.IsEnded) return null;

        var toReturn = returnedAttacker ?? FindUnblockedAttacker(caster, combat);
        if (toReturn == null) return null;
        // The nominated attacker must be a legal (unblocked, caster-controlled)
        // attacker in the current combat (CR 702.49e).
        if (!IsUnblockedAttacker(toReturn, caster, combat)) return null;

        // CR 702.49e / CR 506.4 — remove the returned attacker from combat and
        // bounce it to its owner's hand.
        combat.CurrentCombat.RemoveAttacker(toReturn);
        ReturnToHand(toReturn, zoneService);

        // CR 702.49b — put the Ninja onto the battlefield from hand. Move it to
        // the battlefield first (so it is a battlefield permanent), then splice
        // it into combat tapped and attacking the same defender.
        if (zoneService != null)
        {
            zoneService.MoveCard(ninja, ZoneType.Hand, ZoneType.Battlefield, caster);
        }
        else
        {
            caster.Zones.Hand.RemoveCard(ninja);
            caster.Zones.Battlefield.AddCard(ninja);
            ninja.SetZone(ZoneType.Battlefield);
            ninja.SetController(caster);
        }

        // CR 702.49b/d — enters tapped and attacking the same defender as the
        // combat it joins. CombatManager.AddTappedAndAttackingToken taps the
        // creature and adds it via Combat.AddAttackerInProgress.
        return combat.AddTappedAndAttackingToken(ninja);
    }

    /// <summary>
    /// The first unblocked attacker <paramref name="caster"/> controls in the
    /// current combat, or null when there is none.
    /// </summary>
    public static Creature? FindUnblockedAttacker(Player caster, CombatManager combat)
    {
        var current = combat?.CurrentCombat;
        if (current == null || current.IsEnded) return null;

        foreach (var attacker in current.Attackers)
        {
            if (attacker.Blockers.Count > 0) continue; // blocked
            if (!ReferenceEquals(attacker.Creature.Controller, caster)) continue;
            return attacker.Creature;
        }
        return null;
    }

    private static bool IsUnblockedAttacker(Creature creature, Player caster, CombatManager combat)
    {
        var current = combat.CurrentCombat;
        if (current == null) return false;
        foreach (var attacker in current.Attackers)
        {
            if (!ReferenceEquals(attacker.Creature, creature)) continue;
            if (attacker.Blockers.Count > 0) return false;
            return ReferenceEquals(attacker.Creature.Controller, caster);
        }
        return false;
    }

    private static void ReturnToHand(Creature creature, ZoneService? zoneService)
    {
        var owner = creature.Owner ?? creature.Controller;
        if (owner == null) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(creature, ZoneType.Battlefield, ZoneType.Hand, owner);
        }
        else
        {
            var controller = creature.Controller;
            controller?.Zones.Battlefield.RemoveCard(creature);
            owner.Zones.Hand.AddCard(creature);
            creature.SetZone(ZoneType.Hand);
        }
    }
}
