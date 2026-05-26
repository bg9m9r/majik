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
            if (creature.Zone != ZoneType.Battlefield) continue;
            if (CombatAbilities.HasIndestructible(creature)) continue;
            // CR 702.12 / 613.1f — creatures granted indestructible by an
            // external anthem (Darksteel Forge on an Artifact Creature)
            // also resist the lethal-damage / zero-toughness destroy SBA.
            if (Majik.Core.Rules.IndestructibleGrantRegistry.HasGrantedIndestructible(creature)) continue;

            var dies = creature.IsDead() || creature.MarkedForDestructionByDeathtouch;
            if (!dies) continue;

            if (ctx.Replacements != null)
            {
                var result = ctx.Replacements.Apply(new DestroyIntent(creature));
                if (result == null)
                {
                    creature.MarkedForDestructionByDeathtouch = false;
                    continue;
                }
            }

            if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(creature, ZoneType.Graveyard);
            else creature.SetZone(ZoneType.Graveyard);

            ctx.EventBus?.Publish(new StateBasedActionExecutedEvent($"Creature {creature.Name} died"));
            anyExecuted = true;
        }
        return anyExecuted;
    }
}
