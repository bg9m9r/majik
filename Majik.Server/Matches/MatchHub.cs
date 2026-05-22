using Majik.Server.Composition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Matches;

[Authorize(Policy = AuthRegistration.AsPlayerPolicy)]
public sealed class MatchHub : Hub
{
    private readonly MatchRepository _matches;

    public MatchHub(MatchRepository matches) { _matches = matches; }

    public async Task JoinMatch(Guid matchId)
    {
        var match = await _matches.GetByIdAsync(matchId, Context.ConnectionAborted);
        if (match == null) throw new HubException($"Match {matchId} not found.");

        var sub = Context.User?.FindFirst("sub")?.Value;
        if (sub == null) throw new HubException("Connection has no sub claim.");

        // Only the seated players (creator + opponent) may subscribe to the
        // match group. Previously public matches let any authenticated user
        // join the group, which — combined with hub broadcasts that include
        // per-player hidden zones (CR 706 hand + library) — leaked god-view
        // state to third parties. No spectators for now; revisit when the
        // broadcast payloads are routed through PublishPerRecipient.
        var isParty = match.Creator.Sub == sub || match.Opponent?.Sub == sub;
        if (!isParty)
            throw new HubException("Not a participant in this match.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(matchId));
    }

    public Task LeaveMatch(Guid matchId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(matchId));

    internal static string GroupName(Guid matchId) => $"match:{matchId}";
}
