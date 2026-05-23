using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("Majik.Server.Tests")]

namespace Majik.Server.Matches;

/// <summary>
/// Bridges a <see cref="GameFacade"/> to the SignalR match group so the
/// frontend can observe live engine activity. For each attached match we
/// hold two subscriptions:
///
/// <list type="bullet">
///   <item><description>
///     <c>facade.SubscribeEnvelopes(EventEnvelope)</c> — forwarded to
///     the <c>"event"</c> channel. The envelope carries the
///     full-reveal <see cref="EventDto"/> in <see cref="EventEnvelope.Public"/>
///     and, for CR 706 hidden-information events (e.g. card moves
///     between hand / library, draws), per-player masked variants in
///     <see cref="EventEnvelope.PerPlayer"/>. When per-player variants
///     are present the bridge calls
///     <see cref="IMatchHubPublisher.PublishPerRecipient"/> so each seat
///     receives ONLY its own scoped payload — group fan-out is bypassed
///     entirely to avoid leaking the unmasked variant to the opponent.
///     For purely public events PerPlayer is null and the bridge falls
///     through to a single group broadcast (cheaper than two
///     per-recipient sends).
///   </description></item>
///   <item><description>
///     <c>facade.SubscribePrompts(PromptDto)</c> — forwarded to the
///     <c>"prompt"</c> channel via per-recipient delivery so ONLY the
///     player named by <see cref="PromptDto.PlayerId"/> receives the
///     envelope. Mapping PlayerId → recipient sub uses
///     <see cref="GameFacade.Alice"/> = creator, <see cref="GameFacade.Bob"/>
///     = opponent, matching the convention used everywhere else (see
///     <see cref="ServerGameFactory.Create"/> and
///     <see cref="MatchService.GetGameStateAsync"/>). A prompt for the
///     creator is sent only to the creator's user channel; the opponent
///     never sees it, which avoids leaking turn-timing tells over the
///     wire even though PromptDto itself carries no card data.
///   </description></item>
/// </list>
///
/// The bridge holds the <see cref="IDisposable"/> returned by each
/// subscribe call. <see cref="Detach"/> disposes both, severing the
/// engine→hub link before the facade is removed from
/// <see cref="ServerGameFactory.Delete"/>. Callers must invoke
/// <see cref="Detach"/> on every terminal match state (Completed,
/// Abandoned, timeout) — leaking subscriptions would pin the facade in
/// memory long after the match document is gone.
/// </summary>
public sealed class MatchFacadeBridge
{
    private readonly IMatchHubPublisher _hub;
    private readonly ILogger<MatchFacadeBridge> _logger;
    private readonly MatchReplayBuffer? _replay;
    private readonly ConcurrentDictionary<Guid, Attachment> _attachments = new();

    // Per-recipient prompt buffer. Solves the race where the engine
    // publishes a prompt to the match group BEFORE the targeted player's
    // SignalR connection has joined (most acute on vs-Bot matches where
    // StartFullGameAsync runs synchronously to the first agent prompt
    // inside the CreateBotMatchAsync HTTP handler, well before the client
    // has navigated to /match/:id and called JoinMatch). The buffer is
    // keyed by (matchId, recipientSub) — never by PlayerId — so the same
    // recipient is overwritten when a fresh prompt for them arrives, and
    // a NULL/unknown PlayerId entry can't be addressed by mistake. Bot
    // recipients ("bot:*") are skipped at insert time, both to save space
    // and to make AckPrompt's contract symmetric with ForwardPrompt's
    // bot-skip.
    private readonly ConcurrentDictionary<(Guid MatchId, string Sub), PromptDto> _bufferedPrompts = new();

    public MatchFacadeBridge(
        IMatchHubPublisher hub,
        ILogger<MatchFacadeBridge> logger,
        MatchReplayBuffer? replay = null)
    {
        _hub = hub;
        _logger = logger;
        _replay = replay;
    }

    /// <summary>Visible for tests.</summary>
    internal int ActiveCount => _attachments.Count;

    /// <summary>Visible for tests — true iff this matchId currently has
    /// live subscriptions held by the bridge.</summary>
    internal bool IsAttached(Guid matchId) => _attachments.ContainsKey(matchId);

    /// <summary>Visible for tests — current size of the per-recipient
    /// prompt buffer. Exposed so tests can assert Detach clears the
    /// buffer without reaching through reflection.</summary>
    internal int BufferedPromptCount => _bufferedPrompts.Count;

    /// <summary>Visible for tests — peek the buffered prompt for a given
    /// (matchId, recipient sub) tuple. Returns null when there is no
    /// buffered prompt for that recipient.</summary>
    internal PromptDto? PeekBufferedPrompt(Guid matchId, string recipientSub) =>
        _bufferedPrompts.TryGetValue((matchId, recipientSub), out var prompt) ? prompt : null;

    /// <summary>
    /// Subscribe the bridge to <paramref name="facade"/> on behalf of
    /// <paramref name="matchId"/>. Idempotent: a second call with the
    /// same matchId tears down the previous attachment before installing
    /// the new one, so stale subscriptions from a re-created facade
    /// can't fan out into the hub group.
    /// </summary>
    public void Attach(Guid matchId, string creatorSub, string opponentSub, GameFacade facade)
    {
        ArgumentNullException.ThrowIfNull(creatorSub);
        ArgumentNullException.ThrowIfNull(opponentSub);
        ArgumentNullException.ThrowIfNull(facade);

        // Detach any prior attachment to avoid double-subscribing on a
        // re-created facade for the same match id.
        Detach(matchId);

        var routing = new PromptRouting(facade.Alice.Id, facade.Bob.Id, creatorSub, opponentSub);

        IDisposable eventSub = facade.SubscribeEnvelopes(env => ForwardEvent(matchId, env, routing));
        IDisposable promptSub = facade.SubscribePrompts(prompt => ForwardPrompt(matchId, prompt, routing));

        var attachment = new Attachment(eventSub, promptSub);
        if (!_attachments.TryAdd(matchId, attachment))
        {
            // Lost the race against another Attach for the same matchId —
            // dispose what we just created so we don't leak.
            attachment.Dispose();
        }
    }

    /// <summary>
    /// Dispose the subscriptions for <paramref name="matchId"/> if any.
    /// Safe to call multiple times — terminal-state callers (concede,
    /// abandon, timeout, completion sweep) all funnel through here and
    /// it must remain idempotent for that reason.
    ///
    /// Also clears any per-recipient prompt buffers held for this match.
    /// A terminal state means no further commands can be submitted, so
    /// replaying an unacked prompt to a late-joining connection after
    /// teardown would just confuse the client.
    /// </summary>
    public void Detach(Guid matchId)
    {
        if (_attachments.TryRemove(matchId, out var attachment))
        {
            attachment.Dispose();
        }

        // Sweep buffered prompts for this match. ConcurrentDictionary
        // doesn't have a bulk-remove-by-prefix, but the cardinality is
        // bounded (≤ 2 entries per active match — one per seat) so a
        // full key scan is cheap and stays correct under concurrent
        // forwards/acks happening on other matches.
        foreach (var key in _bufferedPrompts.Keys)
        {
            if (key.MatchId == matchId)
            {
                _bufferedPrompts.TryRemove(key, out _);
            }
        }

        // Seal the replay buffer — match is over (concede / abandon /
        // timeout / completion all funnel through Detach), so no further
        // events should append to the replay log. The buffer itself is
        // retained for download until LRU evicts it; see
        // MatchReplayBuffer for the retention contract.
        _replay?.Seal(matchId);
    }

    /// <summary>
    /// Drop the buffered prompt (if any) for the given recipient on the
    /// given match. Called from <c>MatchService.SubmitCommandAsync</c>
    /// after the engine has accepted a command — that command resolves
    /// the TCS the engine is waiting on, so the previously-published
    /// prompt is no longer authoritative. If the engine then emits a
    /// fresh prompt for the same recipient, <see cref="ForwardPrompt"/>
    /// will rebuffer it.
    ///
    /// No-ops on bot recipients (no buffer to clear) and on
    /// unrecognized (matchId, sub) pairs (concurrent Detach can race).
    /// </summary>
    public void AckPrompt(Guid matchId, string recipientSub)
    {
        if (string.IsNullOrEmpty(recipientSub)) return;
        if (recipientSub.StartsWith("bot:", StringComparison.Ordinal)) return;
        _bufferedPrompts.TryRemove((matchId, recipientSub), out _);
    }

    /// <summary>
    /// If a prompt is currently buffered for <paramref name="recipientSub"/>
    /// on <paramref name="matchId"/>, push it to the single SignalR
    /// connection identified by <paramref name="connectionId"/>. Called
    /// from <c>MatchHub.JoinMatch</c> immediately after the connection
    /// is added to the match group, so a client that navigates to the
    /// match page AFTER the engine has already published an early prompt
    /// (the bot-match opening-mulligan race) still receives it.
    ///
    /// Group-fanout is intentionally avoided here: if both seats have
    /// already joined when one of them refreshes, the OTHER seat's
    /// buffered prompt must not be re-fanned to the refreshing player.
    /// </summary>
    public void ReplayPromptIfAny(Guid matchId, string recipientSub, string connectionId)
    {
        if (string.IsNullOrEmpty(recipientSub)) return;
        if (string.IsNullOrEmpty(connectionId)) return;
        if (recipientSub.StartsWith("bot:", StringComparison.Ordinal)) return;

        if (!_bufferedPrompts.TryGetValue((matchId, recipientSub), out var prompt))
        {
            return;
        }

        try
        {
            _hub.SendToConnection(connectionId, "prompt", prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MatchFacadeBridge: failed to replay buffered prompt. " +
                "MatchId={MatchId} RecipientSub={RecipientSub} ConnectionId={ConnectionId}",
                matchId, recipientSub, connectionId);
        }
    }

    // Event-handler core — internal so unit tests can drive forwarding
    // without standing up a full engine. Producing real envelopes from
    // the engine requires a game loop; isolating the routing here lets
    // the test inject a synthesized envelope and assert the resulting
    // hub publish.
    //
    // Routing:
    //   * envelope.PerPlayer == null  → group broadcast on "event".
    //     The payload is identical for every viewer, so a single fan-out
    //     is correct and cheaper than two per-recipient sends.
    //   * envelope.PerPlayer != null  → per-recipient publish, mapping
    //     each seated sub (creator + opponent) to its viewer-scoped
    //     EventDto. CR 706 hidden-info events (CardMovedEvent /
    //     CardDrawnEvent into a hidden zone) take this path so the
    //     unmasked variant never reaches the opponent.
    internal void ForwardEvent(Guid matchId, EventEnvelope envelope, PromptRouting routing)
    {
        // Capture BEFORE publish so a hub-publish fault doesn't lose the
        // record from the replay log. Capture is best-effort — the buffer
        // swallows its own exceptions, so the live broadcast can't be
        // perturbed by a replay-side failure. The replay buffer stores
        // the full-reveal Public variant — replay is a spectator surface
        // (no per-viewer masking), matching the StateSnapshotter
        // viewer == null path used by GET /matches/{id}/replay.
        _replay?.RecordEvent(matchId, envelope.Public);

        try
        {
            if (envelope.PerPlayer == null)
            {
                _hub.Publish(matchId, "event", envelope.Public);
                return;
            }

            var recipients = new List<string>(capacity: 2);
            if (!string.IsNullOrEmpty(routing.CreatorSub)
                && !routing.CreatorSub.StartsWith("bot:", StringComparison.Ordinal))
            {
                recipients.Add(routing.CreatorSub);
            }
            if (!string.IsNullOrEmpty(routing.OpponentSub)
                && !routing.OpponentSub.StartsWith("bot:", StringComparison.Ordinal))
            {
                recipients.Add(routing.OpponentSub);
            }

            if (recipients.Count == 0) return;

            _hub.PublishPerRecipient(
                matchId,
                "event",
                recipients,
                sub =>
                {
                    var playerId = routing.ResolvePlayerIdForSub(sub);
                    if (playerId.HasValue && envelope.PerPlayer.TryGetValue(playerId.Value, out var dto))
                    {
                        return dto;
                    }
                    // Fall back to the public variant if routing can't
                    // map the recipient — better than dropping the event
                    // and leaving the UI stuck on a stale snapshot.
                    return envelope.Public;
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MatchFacadeBridge: failed to forward EventDto. MatchId={MatchId} EventType={EventType}",
                matchId, envelope.Public.Type);
        }
    }

    // Prompt-handler core — same testing rationale as ForwardEvent. The
    // PromptRouting carries the Alice/Bob → creator/opponent sub mapping
    // captured at Attach time so prompt routing is a pure function of
    // the input PromptDto.PlayerId.
    internal void ForwardPrompt(Guid matchId, PromptDto prompt, PromptRouting routing)
    {
        try
        {
            string? recipient = routing.ResolveRecipientSub(prompt.PlayerId);

            if (recipient == null)
            {
                _logger.LogWarning(
                    "MatchFacadeBridge: dropping prompt for unknown PlayerId. MatchId={MatchId} PlayerId={PlayerId}",
                    matchId, prompt.PlayerId);
                return;
            }

            // Bot seats (sub starts with "bot:") have no SignalR
            // connection; sending to that user channel is a no-op, not
            // an error — skip the hop entirely so we don't burn a
            // per-recipient send for nothing. Also skip the per-recipient
            // buffer: an in-process bot agent has no late-join story.
            if (recipient.StartsWith("bot:", StringComparison.Ordinal))
            {
                return;
            }

            // Buffer BEFORE publishing so a connection that joins the
            // match group between this AddOrUpdate and the PublishPerRecipient
            // call still finds the prompt on its replay-lookup. The
            // entry replaces any prior buffered prompt for this
            // recipient — a stale prompt for a player who never acked
            // it would otherwise survive across the new one.
            _bufferedPrompts[(matchId, recipient)] = prompt;

            _hub.PublishPerRecipient(
                matchId,
                "prompt",
                new[] { recipient },
                _ => prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MatchFacadeBridge: failed to forward PromptDto. MatchId={MatchId} PlayerId={PlayerId}",
                matchId, prompt.PlayerId);
        }
    }

    /// <summary>Maps engine player slot ids → match sub claims. Captured
    /// once at <see cref="Attach"/> so per-prompt routing stays pure.</summary>
    internal readonly record struct PromptRouting(Guid AliceId, Guid BobId, string CreatorSub, string OpponentSub)
    {
        public string? ResolveRecipientSub(Guid playerId)
        {
            if (playerId == AliceId) return CreatorSub;
            if (playerId == BobId) return OpponentSub;
            return null;
        }

        /// <summary>Reverse mapping used by per-recipient event routing —
        /// the publish callback receives a recipient sub and needs to
        /// pick the matching per-player EventDto out of the envelope.</summary>
        public Guid? ResolvePlayerIdForSub(string sub)
        {
            if (sub == CreatorSub) return AliceId;
            if (sub == OpponentSub) return BobId;
            return null;
        }
    }

    private sealed class Attachment : IDisposable
    {
        private readonly IDisposable _eventSub;
        private readonly IDisposable _promptSub;
        private int _disposed;

        public Attachment(IDisposable eventSub, IDisposable promptSub)
        {
            _eventSub = eventSub;
            _promptSub = promptSub;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _eventSub.Dispose(); } catch { /* swallow — disposing on teardown */ }
            try { _promptSub.Dispose(); } catch { /* swallow — disposing on teardown */ }
        }
    }
}
