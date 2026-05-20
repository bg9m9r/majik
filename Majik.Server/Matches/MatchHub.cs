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

        // Public matches: anyone authed can subscribe (for read-only future).
        // Invite matches: must be creator or opponent.
        var isParty = match.Creator.Sub == sub || match.Opponent?.Sub == sub;
        if (match.Visibility == MatchVisibility.Invite && !isParty)
            throw new HubException("Private match — not a participant.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(matchId));
    }

    public Task LeaveMatch(Guid matchId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(matchId));

    internal static string GroupName(Guid matchId) => $"match:{matchId}";
}
