using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5r — Saga with all chapters complete → sacrifice.</summary>
public sealed class SagaSacrificedCheck : IStateBasedActionCheck
{
    public string Name => "SagaSacrificed";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var perm in ctx.Cards.OfType<Permanent>().ToList())
        {
            if (perm.Zone != ZoneType.Battlefield) continue;
            if (perm.SagaState == null) continue;
            if (!perm.SagaState.ShouldBeSacrificed()) continue;

            if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(perm, ZoneType.Graveyard);
            else perm.SetZone(ZoneType.Graveyard);
            ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                $"Saga {perm.Name} sacrificed — final chapter complete"));
            anyExecuted = true;
        }
        return anyExecuted;
    }
}
