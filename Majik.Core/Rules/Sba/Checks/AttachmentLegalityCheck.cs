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
        foreach (var perm in ctx.Permanents)
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            var isAura = perm.HasType(CardType.Enchantment) && perm.HasSubtype(CardSubtype.Aura);
            if (perm.AttachedTo == null)
            {
                // CR 704.5m — an Aura not attached to an object or player is
                // put into its owner's graveyard (e.g. Pacifism after its
                // creature was flickered: the returning creature is a new
                // object, CR 400.7). Every path that puts an Aura onto the
                // battlefield attaches it before SBAs are next checked
                // (Aura spells and Animate Dead before landing; effects such
                // as ReturnAsAuraOnDeathEffect in the same effect). A Curse
                // enchants a player, which the engine tracks outside
                // AttachedTo, so it is attached to something. A bestowed card
                // is not printed as an Aura (BestowKeyword) and reverts to a
                // creature instead (CR 702.103f). Equipment attached to
                // nothing simply stays (CR 301.5c).
                if (!isAura || perm.HasSubtype(CardSubtype.Curse)) continue;
                MoveAuraToGraveyard(ctx, perm, "not attached to anything");
                anyExecuted = true;
                continue;
            }
            if (perm.AttachedTo.Zone == ZoneType.Battlefield) continue;

            if (isAura)
            {
                perm.Unattach();
                MoveAuraToGraveyard(ctx, perm, "no legal attachment");
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

    private static void MoveAuraToGraveyard(SbaContext ctx, Permanent aura, string why)
    {
        if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(aura, ZoneType.Graveyard);
        else aura.SetZone(ZoneType.Graveyard);
        ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
            $"Aura {aura.Name} put into graveyard — {why}"));
    }
}
