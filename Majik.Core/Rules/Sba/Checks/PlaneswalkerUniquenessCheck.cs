using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5m — a player controlling 2+ planeswalkers with the
/// same planeswalker subtype chooses one to keep; the rest go to
/// graveyard. Same earliest-entered heuristic as the legend rule until
/// the player-choice prompt lands.</summary>
public sealed class PlaneswalkerUniquenessCheck : IStateBasedActionCheck
{
    public string Name => "PlaneswalkerUniqueness";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        var planeswalkers = ctx.Cards.OfType<Planeswalker>()
            .Where(p => p.Zone == ZoneType.Battlefield)
            .ToList();

        var groups = planeswalkers
            .Select(p => new
            {
                Planeswalker = p,
                Subtype = p.Subtypes.FirstOrDefault(s => IsPlaneswalkerSubtype(s)),
                p.Controller,
            })
            .Where(x => x.Subtype != default(CardSubtype))
            .GroupBy(x => new { x.Subtype, x.Controller })
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var sorted = group
                .OrderBy(x => x.Planeswalker.EnteredBattlefieldTimestamp ?? DateTime.MaxValue)
                .ToList();
            foreach (var entry in sorted.Skip(1))
            {
                var pw = entry.Planeswalker;
                if (ctx.ZoneService != null) ctx.ZoneService.MoveCardTo(pw, ZoneType.Graveyard);
                else pw.SetZone(ZoneType.Graveyard);

                ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                    $"Planeswalker uniqueness rule: {pw.Name} put into graveyard (controlled by {pw.Controller?.Name})"));
                anyExecuted = true;
            }
        }

        return anyExecuted;
    }

    private static bool IsPlaneswalkerSubtype(CardSubtype subtype)
        => subtype is CardSubtype.Ajani
            or CardSubtype.Chandra
            or CardSubtype.Jace
            or CardSubtype.Liliana
            or CardSubtype.Garruk
            or CardSubtype.Nissa
            or CardSubtype.Teferi
            or CardSubtype.Karn
            or CardSubtype.Ugin
            or CardSubtype.Bolas;
}
