using Majik.Server.Composition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Matches;

/// <summary>User-scoped push channel. No methods — connections are routed by
/// sub via <see cref="SubUserIdProvider"/>, so the server reaches a user with
/// <c>Clients.User(sub)</c>. The client just connects + listens for
/// "report-delivered".</summary>
[Authorize(Policy = AuthRegistration.AsPlayerPolicy)]
public sealed class NotificationsHub : Hub { }
