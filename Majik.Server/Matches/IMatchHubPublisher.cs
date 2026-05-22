namespace Majik.Server.Matches;

/// <summary>Abstraction for broadcasting SignalR events to match groups.
/// Null in tests; wired to the real hub in production.</summary>
public interface IMatchHubPublisher
{
    /// <summary>Group-broadcast. Use ONLY for public/aggregate payloads
    /// (state changes, clock ticks, dice rolls). Any event whose payload
    /// carries per-player hidden info (CR 706 hand/library zones) MUST
    /// go through <see cref="PublishPerRecipient"/> instead — group
    /// fan-out would leak the opponent's hidden zones to the other seat.</summary>
    void Publish(Guid matchId, string @event, object payload);

    /// <summary>Per-recipient publish for payloads that carry hidden info.
    /// <paramref name="payloadFor"/> is invoked once per seated sub
    /// (creator + opponent) and must return a snapshot scoped to that
    /// recipient (e.g. masked opponent hand). The result is sent via
    /// <c>Clients.User(sub)</c> so each player gets their own view.
    /// Default impl maps to <see cref="Publish"/> so test fakes don't
    /// need to be updated; production wiring overrides this.</summary>
    void PublishPerRecipient(
        Guid matchId,
        string @event,
        IReadOnlyList<string> recipientSubs,
        Func<string, object> payloadFor)
    {
        foreach (var sub in recipientSubs)
        {
            if (string.IsNullOrEmpty(sub)) continue;
            Publish(matchId, @event, payloadFor(sub));
        }
    }

    /// <summary>Broadcasts the bot's "thinking" state for a vs-Bot match.
    /// Frontend can render a spinner / typing indicator while the engine
    /// awaits the bot agent's decision. Wiring from BotPlayerAgent → this
    /// publisher is future work; the method exists so callers can publish
    /// once the seam is in place. Default impl delegates to
    /// <see cref="Publish"/> so existing fakes don't need updating.</summary>
    void PublishBotThinking(Guid matchId, bool thinking)
        => Publish(matchId, "match.bot-thinking", new { matchId, thinking });
}
