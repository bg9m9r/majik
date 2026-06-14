using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5f — creatures with lethal damage or 0 toughness die.
/// Routed through the replacement bus first so regeneration shields and
/// totem armor (CR 614) can cancel the destroy.</summary>
public sealed class CreatureDeathCheck : IStateBasedActionCheck
{
    public string Name => "CreatureDeath";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        // Shared per-pass projection (materialized once on SbaContext) rather
        // than a fresh OfType<Creature>().ToList() per check. Membership by CLR
        // type is stable for the pass, so iterating it while the loop moves
        // cards to the graveyard is equivalent to the old defensive copy.
        var creatures = ctx.Creatures;
        for (var i = 0; i < creatures.Count; i++)
        {
            if (TryDestroyCreature(creatures[i], ctx)) anyExecuted = true;
        }

        // CR 613.1c / 704.5f / 711 — an animated NON-creature C# instance (a
        // manland: a Land computing as a creature via a Layer-4 type grant; a
        // Karn-animated artifact) is NOT in ctx.Creatures (it's not a Creature
        // instance) but IS effectively a creature, so the lethal-damage /
        // 0-toughness destroy SBA must reach it too. It carries its combat body
        // through the lifted Permanent-level surface (GetEffectiveToughness /
        // MarkedDamage / HasLethalMarkedDamage). Scan permanents that are
        // effectively-but-not-instance creatures; real Creatures are handled
        // above, and a permanent carrying a loyalty body (a planeswalker) is
        // governed by the planeswalker-death SBA, not this one.
        var permanents = ctx.Permanents;
        for (var i = 0; i < permanents.Count; i++)
        {
            var perm = permanents[i];
            if (perm is Creature) continue;
            if (perm.IsEffectivePlaneswalker()) continue;
            if (!perm.IsEffectivelyCreature()) continue;
            if (TryDestroyEffectiveCreaturePermanent(perm, ctx)) anyExecuted = true;
        }

        return anyExecuted;
    }

    /// <summary>CR 704.5f / 711 — destroy an animated NON-creature permanent
    /// (a manland / animated artifact computing as a creature) that has lethal
    /// marked damage or 0 effective toughness, reading the lifted
    /// <see cref="Permanent"/>-level combat-body surface. Mirrors
    /// <see cref="TryDestroyCreature"/> for the non-Creature case. Returns true
    /// when the SBA actually fired.</summary>
    private static bool TryDestroyEffectiveCreaturePermanent(Permanent perm, SbaContext ctx)
    {
        if (perm.Zone != ZoneType.Battlefield) return false;
        // CR 702.12 — indestructible (intrinsic layer-granted OR externally
        // granted) resists the destroy SBA. The intrinsic check reads the
        // layer-computed keyword set directly (CombatAbilities.HasIndestructible
        // is Creature-typed; this permanent is a non-Creature instance).
        if (perm.ActiveEffects != null &&
            perm.ActiveEffects.Compute(perm).Keywords.Contains("Indestructible")) return false;
        if (Majik.Core.Rules.IndestructibleGrantRegistry.HasGrantedIndestructible(perm)) return false;

        // CR 704.5f — a creature with toughness 0 dies; with lethal marked
        // damage (or deathtouch-marked) dies. HasLethalMarkedDamage folds in the
        // deathtouch flag; the explicit 0-toughness check covers an animated
        // body whose set-base toughness is 0 (no damage needed).
        var zeroToughness = perm.GetEffectiveToughness() <= 0;
        var dies = zeroToughness || perm.HasLethalMarkedDamage();
        if (!dies) return false;

        if (ctx.Replacements != null)
        {
            var result = ctx.Replacements.Apply(new DestroyIntent(perm));
            if (result == null)
            {
                perm.MarkedForDestructionByDeathtouch = false;
                return false;
            }
        }

        if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(perm, ZoneType.Graveyard);
        else perm.SetZone(ZoneType.Graveyard);

        ctx.EventBus?.Publish(new StateBasedActionExecutedEvent($"Creature {perm.Name} died"));
        return true;
    }

    /// <summary>CR 704.5f / 702.12 / 613.1f — destroy <paramref name="creature"/>
    /// if it has lethal damage / 0 toughness, no indestructible (intrinsic or
    /// externally granted), and no replacement intervenes. Returns true when
    /// the SBA actually fired.</summary>
    private static bool TryDestroyCreature(Creature creature, SbaContext ctx)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;
        // CR 704.5f / 711 — this SBA applies only to permanents that are
        // CURRENTLY creatures. A Creature C# instance flipped to a non-creature
        // DFC back (a planeswalker back) is not effectively a creature: it has
        // no toughness, so the lethal/0-toughness rule must not touch it (its
        // death is governed by the planeswalker-death SBA via IsLoyaltyDead).
        if (!creature.IsEffectivelyCreature()) return false;
        if (CombatAbilities.HasIndestructible(creature)) return false;
        // CR 702.12 / 613.1f — externally-granted indestructible (Darksteel
        // Forge on an Artifact Creature) also resists the destroy SBA.
        if (Majik.Core.Rules.IndestructibleGrantRegistry.HasGrantedIndestructible(creature)) return false;

        var dies = creature.IsDead() || creature.MarkedForDestructionByDeathtouch;
        if (!dies) return false;

        if (ctx.Replacements != null)
        {
            var result = ctx.Replacements.Apply(new DestroyIntent(creature));
            if (result == null)
            {
                creature.MarkedForDestructionByDeathtouch = false;
                return false;
            }
        }

        if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(creature, ZoneType.Graveyard);
        else creature.SetZone(ZoneType.Graveyard);

        ctx.EventBus?.Publish(new StateBasedActionExecutedEvent($"Creature {creature.Name} died"));
        return true;
    }
}
