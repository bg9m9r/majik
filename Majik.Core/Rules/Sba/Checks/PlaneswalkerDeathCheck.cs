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
        // CR 704.5j — a planeswalker with 0 loyalty dies. The loyalty body can
        // live on a real Planeswalker C# instance OR on the transient-loyalty
        // surface of a creature-front transform DFC flipped to its planeswalker
        // back (CR 711). Both expose Permanent.IsLoyaltyDead(), so this check is
        // keyed on the effective loyalty body, not the concrete Planeswalker
        // subclass. A permanent with no loyalty body (a plain creature) returns
        // false and is skipped.
        foreach (var pw in ctx.Permanents)
        {
            if (pw.Zone != ZoneType.Battlefield) continue;
            if (!pw.IsLoyaltyDead()) continue;

            if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(pw, ZoneType.Graveyard);
            else pw.SetZone(ZoneType.Graveyard);

            ctx.EventBus?.Publish(new StateBasedActionExecutedEvent($"Planeswalker {pw.Name} died"));
            anyExecuted = true;
        }
        return anyExecuted;
    }
}
