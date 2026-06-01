using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5k — a player controlling 2+ legendary permanents
/// with the same name chooses one to keep; the rest go to graveyard.
/// Current heuristic: keep the earliest-entered (timestamp tie-break)
/// until the player-choice prompt is wired through the API layer.</summary>
public sealed class LegendRuleCheck : IStateBasedActionCheck
{
    public string Name => "LegendRule";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        var permanents = ctx.Permanents
            .Where(p => p.Zone == ZoneType.Battlefield)
            // CR 704.5k / CR 205.4 — read the EFFECTIVE supertype set so a
            // permanent that "is legendary" via a Layer-4 grant (the
            // Ring-bearer designation routed through GrantSupertypeEffect)
            // counts, and stops counting when the grant is revoked. Falls
            // back to printed supertypes when no layer service is wired.
            .Where(p => p.HasEffectiveSupertype(CardSupertype.Legendary))
            .ToList();

        var groups = permanents
            .GroupBy(p => new { p.Name, p.Controller })
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var sorted = group.OrderBy(p => p.EnteredBattlefieldTimestamp ?? DateTime.MaxValue).ToList();
            foreach (var perm in sorted.Skip(1))
            {
                if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(perm, ZoneType.Graveyard);
                else perm.SetZone(ZoneType.Graveyard);

                ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"Legend rule: {perm.Name} put into graveyard (controlled by {perm.Controller?.Name})"));
                anyExecuted = true;
            }
        }

        return anyExecuted;
    }
}
