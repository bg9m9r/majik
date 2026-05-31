using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5d — a token in a zone other than the battlefield
/// ceases to exist (removed from its current zone, not moved).</summary>
public sealed class TokensCeaseToExistCheck : IStateBasedActionCheck
{
    public string Name => "TokensCeaseToExist";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var perm in ctx.Permanents)
        {
            if (!perm.IsToken || perm.Zone == ZoneType.Battlefield) continue;
            var zone = perm.Owner?.Zones.GetZone(perm.Zone);
            if (zone == null || !zone.ContainsCard(perm)) continue;

            zone.RemoveCard(perm);
            ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                $"Token {perm.Name} ceases to exist"));
            anyExecuted = true;
        }
        return anyExecuted;
    }
}
