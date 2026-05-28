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
        foreach (var creature in ctx.Cards.OfType<Creature>().ToList())
        {
            if (TryDestroyCreature(creature, ctx)) anyExecuted = true;
        }
        return anyExecuted;
    }

    /// <summary>CR 704.5f / 702.12 / 613.1f — destroy <paramref name="creature"/>
    /// if it has lethal damage / 0 toughness, no indestructible (intrinsic or
    /// externally granted), and no replacement intervenes. Returns true when
    /// the SBA actually fired.</summary>
    private static bool TryDestroyCreature(Creature creature, SbaContext ctx)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;
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
