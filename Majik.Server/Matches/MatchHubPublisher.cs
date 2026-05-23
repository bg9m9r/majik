using Microsoft.AspNetCore.SignalR;

namespace Majik.Server.Matches;

public sealed class MatchHubPublisher : IMatchHubPublisher
{
    private readonly IHubContext<MatchHub> _hub;
    private readonly ILogger<MatchHubPublisher> _log;

    public MatchHubPublisher(IHubContext<MatchHub> hub, ILogger<MatchHubPublisher> log)
    {
        _hub = hub;
        _log = log;
    }

    public void Publish(Guid matchId, string @event, object payload)
    {
        // Fire-and-forget; orchestrator must not block on hub I/O.
        _ = _hub.Clients.Group(MatchHub.GroupName(matchId))
            .SendAsync(@event, payload)
            .ContinueWith(t =>
            {
                if (t.IsFaulted) _log.LogError(t.Exception, "Hub publish failed: {Event}", @event);
            }, TaskScheduler.Default);
    }

    public void PublishBotThinking(Guid matchId, bool thinking)
        => Publish(matchId, "match.bot-thinking", new { matchId, thinking });

    /// <summary>
    /// Single-connection send used by the prompt-replay path on
    /// <c>MatchHub.JoinMatch</c>. Fire-and-forget so a slow / disconnected
    /// client can't block the join handshake.
    /// </summary>
    public void SendToConnection(string connectionId, string @event, object payload)
    {
        if (string.IsNullOrEmpty(connectionId)) return;
        _ = _hub.Clients.Client(connectionId)
            .SendAsync(@event, payload)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _log.LogError(t.Exception,
                        "Hub send-to-connection failed: {Event} ConnectionId={ConnectionId}",
                        @event, connectionId);
            }, TaskScheduler.Default);
    }
}
