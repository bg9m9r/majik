using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5j — planeswalkers with 0 loyalty die.</summary>
public sealed class PlaneswalkerDeathCheck : IStateBasedActionCheck
{
    public string Name => "PlaneswalkerDeath";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var pw in ctx.Cards.OfType<Planeswalker>().ToList())
        {
            if (pw.Zone != ZoneType.Battlefield) continue;
            if (!pw.IsDead()) continue;

            if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(pw, ZoneType.Graveyard);
            else pw.SetZone(ZoneType.Graveyard);

            ctx.EventBus?.Publish(new StateBasedActionExecutedEvent($"Planeswalker {pw.Name} died"));
            anyExecuted = true;
        }
        return anyExecuted;
    }
}
