using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Matches;

/// <summary>Pushes user-scoped notifications over the NotificationsHub.</summary>
public interface INotificationsPublisher
{
    /// <summary>Tell the reporter (keyed by sub) their report's fix is live.</summary>
    Task NotifyReportDeliveredAsync(string sub, int issueNumber, string title, CancellationToken ct);
}

public sealed class NotificationsPublisher : INotificationsPublisher
{
    private readonly IHubContext<NotificationsHub> _hub;
    public NotificationsPublisher(IHubContext<NotificationsHub> hub) => _hub = hub;

    public Task NotifyReportDeliveredAsync(string sub, int issueNumber, string title, CancellationToken ct) =>
        _hub.Clients.User(sub).SendAsync(
            "report-delivered", new { issueNumber, title, reloadRequired = true }, ct);
}
