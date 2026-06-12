using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Zones;

namespace Majik.Core.Rules.Sba.Checks;

/// <summary>
/// CR 111.7 / 704.5d — a token in a zone other than the battlefield ceases
/// to exist. This is a state-based action.
///
/// <para>A dying / leaving token may MOMENTARILY exist in its destination
/// zone (graveyard, exile, hand, library) so that "dies" / "leaves the
/// battlefield" triggers — and any "whenever a creature dies" watchers —
/// fire off the captured reference and last-known-information can read its
/// characteristics. Those triggers are queued when the token moves (the
/// <c>CardMovedEvent</c> fires on the zone move, before SBAs run). The very
/// next SBA check then removes the token from that zone entirely.</para>
///
/// <para>This check scans the players' non-battlefield zones DIRECTLY rather
/// than relying on the coordinator's card list. The live priority / combat
/// flow only ever hands the SBA coordinator the cards currently on the
/// battlefield, so a token that just died is not in that list — the check
/// must source it from the owning player's zones itself, or it would never
/// observe a lingering token.</para>
/// </summary>
public sealed class TokensCeaseToExistCheck : IStateBasedActionCheck
{
    private static readonly ZoneType[] NonBattlefieldZones =
    {
        ZoneType.Graveyard,
        ZoneType.Exile,
        ZoneType.Hand,
        ZoneType.Library,
        ZoneType.Stack,
        ZoneType.Command,
    };

    public string Name => "TokensCeaseToExist";

    public bool Execute(SbaContext ctx)
    {
        var anyExecuted = false;
        foreach (var player in ctx.Players)
        {
            foreach (var zoneType in NonBattlefieldZones)
            {
                var zone = player.Zones.GetZone(zoneType);
                // Snapshot — RemoveCard mutates the underlying list.
                foreach (var card in zone.GetCards())
                {
                    if (card is not Permanent { IsToken: true }) continue;

                    zone.RemoveCard(card);
                    ctx.EventBus?.Publish(new StateBasedActionExecutedEvent(
                        $"Token {card.Name} ceases to exist"));
                    anyExecuted = true;
                }
            }
        }
        return anyExecuted;
    }
}
