using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5n — Battle with 0 defense counters → graveyard.</summary>
public sealed class BattleDestroyedCheck : IStateBasedActionCheck
{
    public string Name => "BattleDestroyed";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var perm in ctx.Cards.OfType<Permanent>().ToList())
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            if (perm.BattleState == null) continue;
            if (!perm.BattleState.ShouldBeSacrificed()) continue;

            if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(perm, ZoneType.Graveyard);
            else perm.SetZone(ZoneType.Graveyard);
            ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                $"Battle {perm.Name} destroyed — 0 defense"));
            anyExecuted = true;
        }
        return anyExecuted;
    }
}
