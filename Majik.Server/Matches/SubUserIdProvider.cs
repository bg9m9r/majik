using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Matches;

/// <summary>
/// Maps SignalR <c>Clients.User(sub)</c> routing to the JWT "sub" claim.
/// The framework default keys on <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>,
/// which Descope tokens populate to the same value but only via the
/// validator's claim-lift dance — keying directly on "sub" matches the
/// rest of the codebase and avoids relying on that lift for hub
/// addressing (notably <see cref="MatchHubPublisher.PublishPerRecipient"/>).
/// </summary>
public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirst("sub")?.Value;
}
