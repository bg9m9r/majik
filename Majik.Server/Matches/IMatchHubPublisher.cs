namespace Majik.Server.Matches;

/// <summary>Abstraction for broadcasting SignalR events to match groups.
/// Null in tests; wired to the real hub in production.</summary>
public interface IMatchHubPublisher
{
    void Publish(Guid matchId, string @event, object payload);

    /// <summary>Broadcasts the bot's "thinking" state for a vs-Bot match.
    /// Frontend can render a spinner / typing indicator while the engine
    /// awaits the bot agent's decision. Wiring from BotPlayerAgent → this
    /// publisher is future work; the method exists so callers can publish
    /// once the seam is in place. Default impl delegates to
    /// <see cref="Publish"/> so existing fakes don't need updating.</summary>
    void PublishBotThinking(Guid matchId, bool thinking)
        => Publish(matchId, "match.bot-thinking", new { matchId, thinking });
}
