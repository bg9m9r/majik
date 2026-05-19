using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5h/n — Auras illegally attached go to graveyard;
/// Equipment / Fortifications attached to an illegal permanent become
/// unattached.</summary>
public sealed class AttachmentLegalityCheck : IStateBasedActionCheck
{
    public string Name => "AttachmentLegality";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var perm in ctx.Cards.OfType<Permanent>().ToList())
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            if (perm.AttachedTo == null) continue;
            if (perm.AttachedTo.Zone == ZoneType.Battlefield) continue;

            if (perm.HasType(CardType.Enchantment) && perm.HasSubtype(CardSubtype.Aura))
            {
                perm.Unattach();
                if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(perm, ZoneType.Graveyard);
                else perm.SetZone(ZoneType.Graveyard);
                ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"Aura {perm.Name} put into graveyard — no legal attachment"));
            }
            else
            {
                perm.Unattach();
                ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"{perm.Name} unattached — bearer gone"));
            }
            anyExecuted = true;
        }
        return anyExecuted;
    }
}
