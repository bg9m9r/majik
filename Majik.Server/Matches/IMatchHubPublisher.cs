namespace Majik.Server.Matches;

/// <summary>Abstraction for broadcasting SignalR events to match groups.
/// Null in tests; wired to the real hub in production.</summary>
public interface IMatchHubPublisher
{
    void Publish(Guid matchId, string @event, object payload);
}
