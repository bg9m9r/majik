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
}
