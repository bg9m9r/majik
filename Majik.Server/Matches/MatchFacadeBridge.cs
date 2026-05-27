using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    /// <summary>
    /// Callback the bridge fires when the engine's active player changes
    /// seats (CR 117 / 103.7 — "the active player is the player whose turn
    /// it is", and on each new turn that player receives priority first).
    /// Production wires this to <c>MatchService.OnPriorityPassedAsync</c>
    /// via a fresh DI scope (mirroring <see cref="MatchTimeoutScheduler"/>),
    /// so the server clock holder always DERIVES from the engine's active
    /// player instead of staying frozen at the play/draw first player.
    /// </summary>
    /// <param name="expectedPrevHolderSub">
    /// The holder sub the bridge believes currently owns the clock (mapped
    /// from the engine's PREVIOUS active player). Threaded through so the
    /// clock update can be a compare-and-swap on the prior holder — two
    /// out-of-order handoffs can't both bill the same prior holder (C1).
    /// Null on the very first genuine handoff if the prior seat is
    /// unmappable.
    /// </param>
    public delegate Task ClockHandoffCallback(
        Guid matchId, string newHolderSub, string? expectedPrevHolderSub, CancellationToken ct);

    private readonly IMatchHubPublisher _hub;
    private readonly ILogger<MatchFacadeBridge> _logger;
    private readonly MatchReplayBuffer? _replay;
    private readonly ClockHandoffCallback? _onActivePlayerChanged;
    private readonly ConcurrentDictionary<Guid, Attachment> _attachments = new();

    // Prod-safe desync observability (Slice 4b #3). The DEBUG-only
    // AssertAgreement guard is compiled out of Release, so operational
    // desync (clock holder ≠ engine active player, or a raw CR 505 "Main"
    // on the wire) would be invisible in prod. We mirror its two invariants
    // here as a structured WARNING + counter that NEVER throws — a desync
    // is a diagnostic signal, not a reason to abort live delivery.
    //
    // Rate-limited: a single desync would otherwise log on every event for
    // the affected match (the bridge sees every engine event). We log at
    // most once per (matchId, kind) window-cooldown so a stuck match emits
    // a handful of WARNINGs, not a flood. The counter is unthrottled so
    // metrics still reflect the true violation volume.
    private static readonly TimeSpan DesyncLogCooldown = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<(Guid MatchId, string Kind), DateTime> _lastDesyncLogAt = new();
    private long _desyncWarningCount;

    /// <summary>Visible for tests / metrics — total number of desync
    /// violations DETECTED (clock-holder mismatch + raw-"Main"), counting
    /// every detection regardless of log rate-limiting.</summary>
    internal long DesyncWarningCount => Interlocked.Read(ref _desyncWarningCount);

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
        MatchReplayBuffer? replay = null,
        ClockHandoffCallback? onActivePlayerChanged = null)
    {
        _hub = hub;
        _logger = logger;
        _replay = replay;
        _onActivePlayerChanged = onActivePlayerChanged;
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

        // The clock-handoff tracker starts UNSEEDED: the active player at
        // Attach is always Alice (the facade is created before
        // StartFullGameAsync picks who goes first), so seeding from it here
        // would be wrong whenever Bob goes first (draw case / bot wins the
        // roll) — the turn-1 TurnStartedEvent would then look like a Bob→Bob
        // change and burn a spurious slice off the correct first player. We
        // instead seed lazily on the FIRST observed active player (no handoff
        // on the very first turn-start) so the real first player is captured
        // from the engine, not guessed (I2).
        var attachment = new Attachment(eventSub, promptSub, facade, routing);
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

        // Drop the desync rate-limiter entries for this match. The dict is
        // keyed by (matchId, kind) — remove both known kinds so they don't
        // accumulate for the process lifetime (one entry per active match
        // per kind, bounded per-match but never freed without this sweep).
        _lastDesyncLogAt.TryRemove((matchId, "clock-holder"), out _);
        _lastDesyncLogAt.TryRemove((matchId, "raw-main"), out _);

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

    /// <summary>
    /// Push the current per-viewer <see cref="GameStateDto"/> snapshot for
    /// <paramref name="recipientSub"/> to the single SignalR connection
    /// <paramref name="connectionId"/> on the <c>"state"</c> channel. Called
    /// from <c>MatchHub.JoinMatch</c> AFTER the connection is added to the
    /// match group, so a client that joins (or reconnects) AFTER the engine
    /// has already emitted early events (opening draws, mulligan) recovers
    /// authoritative state regardless of which events it missed (Slice 4b
    /// #1). This is the robust fix for the startup race: the client no
    /// longer depends on catching the engine's early fire-and-forget events.
    ///
    /// CR 706 masking is preserved — the snapshot is produced via
    /// <see cref="GameFacade.GetStateFor"/> with the recipient's seat
    /// (Creator → Alice, Opponent → Bob), so the opponent's hand is
    /// masked exactly as <c>MatchService.GetGameStateAsync</c> does.
    ///
    /// No-op (no error) when:
    /// <list type="bullet">
    ///   <item>the game hasn't started yet — no facade attached for the
    ///         match (still Rolling), so there is no snapshot to push (#2);</item>
    ///   <item>the recipient is a bot seat (no SignalR connection); or</item>
    ///   <item>the recipient sub can't be mapped to a seat in the facade.</item>
    /// </list>
    /// Group-fanout is intentionally avoided: the snapshot is per-viewer
    /// (masked) and must reach ONLY the joining connection.
    /// </summary>
    public void ReplaySnapshotIfAny(Guid matchId, string recipientSub, string connectionId)
    {
        if (string.IsNullOrEmpty(recipientSub)) return;
        if (string.IsNullOrEmpty(connectionId)) return;
        if (recipientSub.StartsWith("bot:", StringComparison.Ordinal)) return;

        // Still Rolling / no facade attached → nothing to snapshot. Skipping
        // is correct (the client will get the snapshot when it re-joins
        // after PlayDrawAsync starts the engine and re-renders) — never an
        // error.
        if (!_attachments.TryGetValue(matchId, out var attachment)) return;

        // Map the joining recipient → its engine seat id, then take the
        // per-viewer masked snapshot. Same Creator → Alice / Opponent → Bob
        // convention GetGameStateAsync uses.
        var viewerPlayerId = attachment.Routing.ResolvePlayerIdForSub(recipientSub);
        if (viewerPlayerId == null) return;

        GameStateDto? snapshot;
        try
        {
            snapshot = attachment.Facade.GetStateFor(viewerPlayerId.Value);
        }
        catch (Exception ex)
        {
            // Facade torn down mid-flight (terminal-state Detach raced the
            // join) — nothing to push. Best-effort: log and bail.
            _logger.LogWarning(ex,
                "MatchFacadeBridge: snapshot-on-join faulted reading facade state. " +
                "MatchId={MatchId} RecipientSub={RecipientSub} ConnectionId={ConnectionId}",
                matchId, recipientSub, connectionId);
            return;
        }

        if (snapshot == null) return;

        try
        {
            _hub.SendToConnection(connectionId, "state", snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MatchFacadeBridge: failed to push snapshot-on-join. " +
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

        // CR 117 / 103.7 — keep the server clock holder aligned with the
        // engine's active player. Engine typed handlers update
        // GameFacade._currentActivePlayer BEFORE this global (SubscribeAll)
        // handler runs (see EventBus.Publish ordering), so reading
        // facade.ActivePlayerId here already reflects the new turn's active
        // player. Best-effort and isolated so a clock-side fault never
        // perturbs the live event broadcast.
        MaybeFireClockHandoff(matchId);

        // Prod-safe desync observability (Slice 4b #3). Unlike the DEBUG-only
        // AssertAgreement guard (which throws and is compiled out of Release),
        // this NEVER throws — a throw here would be swallowed by EventBus
        // SafeInvoke anyway. It emits a rate-limited structured WARNING + a
        // counter so operational desync becomes visible in prod:
        //   * clock holder (the seat the bridge last handed the clock to) ≠
        //     engine active player (CR 117 / 103.7), or
        //   * a raw CR 505 "Main" phase/step label reaching the wire.
        MaybeWarnDesync(matchId, envelope);

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

    /// <summary>
    /// CR 117 / 103.7 — when the engine's active player has changed seats
    /// since the last event for <paramref name="matchId"/>, map the new
    /// active player's engine id → sub and invoke the clock-handoff
    /// callback (production: <c>MatchService.OnPriorityPassedAsync</c>),
    /// which decrements the previous holder's clock, moves the holder, and
    /// republishes <c>match.clock-update</c>. The server clock thus DERIVES
    /// its holder from the engine instead of freezing at the play/draw
    /// first player set in <c>MatchService.PlayDrawAsync</c>.
    ///
    /// Fully best-effort: no callback wired (unit tests) or no live
    /// attachment → no-op. The callback is fire-and-forget so the engine's
    /// synchronous event dispatch isn't blocked on a Mongo round-trip;
    /// faults are logged, never thrown back into the event broadcast.
    /// </summary>
    private void MaybeFireClockHandoff(Guid matchId)
    {
        if (_onActivePlayerChanged == null) return;
        if (!_attachments.TryGetValue(matchId, out var attachment)) return;

        Guid current;
        try
        {
            current = attachment.Facade.ActivePlayerId;
        }
        catch
        {
            return; // facade torn down mid-flight — nothing to align.
        }

        // Claim the transition under the attachment's lock so concurrent
        // events for the same match don't double-fire the handoff for one
        // active-player change. (Guid has no Interlocked.Exchange overload,
        // and the engine dispatches a game's events on one thread anyway —
        // a short lock is both correct and cheap.) The FIRST observation
        // only seeds the tracker (no handoff) so the genuine first player —
        // whoever the engine actually chose — is captured rather than the
        // Alice-at-Attach guess (I2).
        if (!attachment.TryClaimActivePlayerChange(current, out var prevActivePlayer)) return;

        // Map engine active-player ids → holder subs (Alice = creator,
        // Bob = opponent). An unmappable NEW id (shouldn't happen) is ignored;
        // an unmappable PREV id just means the CAS expectation is null.
        string? newHolderSub = MapSeatToSub(attachment.Routing, current);
        if (newHolderSub == null) return;

        // The holder the engine is moving AWAY from — threaded to the
        // callback so the clock update is a compare-and-swap on it (C1).
        string? expectedPrevHolderSub = MapSeatToSub(attachment.Routing, prevActivePlayer);

        _ = FireClockHandoffAsync(matchId, newHolderSub, expectedPrevHolderSub);
    }

    /// <summary>Map an engine seat id → holder sub (Alice = creator,
    /// Bob = opponent). Returns null for an unmappable id.</summary>
    private static string? MapSeatToSub(PromptRouting routing, Guid seatId) =>
        seatId == routing.AliceId ? routing.CreatorSub
        : seatId == routing.BobId ? routing.OpponentSub
        : null;

    // -----------------------------------------------------------------------
    // Prod-safe desync observability (Slice 4b #3)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Live-path desync observer driven off <see cref="ForwardEvent"/>.
    /// Extracts the wire phase/step label from the envelope's public payload
    /// and the seat the bridge believes holds the clock, then delegates to
    /// <see cref="ObserveDesync"/>. Best-effort: any fault is swallowed so a
    /// diagnostic side-channel can never perturb the live broadcast.
    /// </summary>
    private void MaybeWarnDesync(Guid matchId, EventEnvelope envelope)
    {
        try
        {
            if (!_attachments.TryGetValue(matchId, out var attachment)) return;

            // The engine's current active player (read post-handoff so it
            // reflects the new turn) and the seat the bridge last handed the
            // clock to. In steady state these are equal — a divergence means
            // a handoff was dropped/stale (CAS no-op), which is exactly the
            // operational desync we want visible.
            Guid activePlayer;
            try { activePlayer = attachment.Facade.ActivePlayerId; }
            catch { return; } // facade torn down mid-flight — nothing to check.

            var clockHolderSeat = attachment.LastHandedOffSeat ?? activePlayer;
            var wirePhase = WirePhaseLabel(envelope.Public);

            ObserveDesync(matchId, activePlayer, clockHolderSeat, wirePhase);
        }
        catch
        {
            // Never let the observer perturb the live broadcast.
        }
    }

    /// <summary>
    /// Prod-safe mirror of <see cref="AssertAgreement"/>: detects the same
    /// two load-bearing violations but LOGS a structured WARNING + bumps a
    /// counter instead of throwing, so operational desync is visible in prod
    /// without aborting live delivery (Slice 4b #3).
    /// <list type="bullet">
    ///   <item>clock holder seat ≠ engine active player (CR 117 / 103.7), or</item>
    ///   <item>a raw, ambiguous CR 505 "Main" label reaching the wire.</item>
    /// </list>
    /// The counter (<see cref="DesyncWarningCount"/>) counts every detection;
    /// the WARNING log is rate-limited per (matchId, kind) so a stuck match
    /// emits a handful of lines, not a flood. Internal so tests can drive a
    /// synthetic mismatch / synthetic raw-"Main" directly.
    /// </summary>
    internal void ObserveDesync(Guid matchId, Guid activePlayerId, Guid clockHolderSeatId, string? wirePhase)
    {
        if (clockHolderSeatId != activePlayerId)
        {
            Interlocked.Increment(ref _desyncWarningCount);
            if (ShouldLogDesync(matchId, "clock-holder"))
            {
                _logger.LogWarning(
                    "DESYNC: clock holder seat ({ClockHolderSeat}) != engine active player " +
                    "({ActivePlayer}) — the server clock must DERIVE its holder from the " +
                    "engine (CR 117 / 103.7). MatchId={MatchId}",
                    clockHolderSeatId, activePlayerId, matchId);
            }
        }

        if (wirePhase == "Main")
        {
            Interlocked.Increment(ref _desyncWarningCount);
            if (ShouldLogDesync(matchId, "raw-main"))
            {
                _logger.LogWarning(
                    "DESYNC: raw phase label \"Main\" reached the wire — it must be " +
                    "disambiguated into PreCombatMain / PostCombatMain before broadcast " +
                    "(CR 505). MatchId={MatchId}",
                    matchId);
            }
        }
    }

    /// <summary>Rate-limit guard for the desync WARNING log. Returns true at
    /// most once per <see cref="DesyncLogCooldown"/> window per (matchId,
    /// kind) so a persistent violation doesn't flood the log. The counter is
    /// bumped unconditionally by the caller — only the log line is throttled.</summary>
    private bool ShouldLogDesync(Guid matchId, string kind)
    {
        var now = DateTime.UtcNow;
        var key = (matchId, kind);
        if (_lastDesyncLogAt.TryGetValue(key, out var last) && now - last < DesyncLogCooldown)
        {
            return false;
        }
        _lastDesyncLogAt[key] = now;
        return true;
    }

    /// <summary>Extract the disambiguated phase/step label from a
    /// PhaseStartedEvent / StepStartedEvent payload, or null when the event
    /// carries no phase/step field. Used by the live desync observer to spot
    /// a raw CR 505 "Main" reaching the wire.</summary>
    private static string? WirePhaseLabel(EventDto evt)
    {
        if (evt.Payload.ValueKind != JsonValueKind.Object) return null;
        // PhaseStarted/Ended carry "phase"; StepStarted/Ended carry "step".
        if (evt.Payload.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.String)
            return phase.GetString();
        if (evt.Payload.TryGetProperty("step", out var step) && step.ValueKind == JsonValueKind.String)
            return step.GetString();
        return null;
    }

    /// <summary>Visible for tests — number of times the clock-handoff
    /// callback has been invoked. Lets a test assert the bridge actually
    /// fired the handoff (vs. the holder lagging for another reason).</summary>
    internal int ClockHandoffFireCount => _clockHandoffFireCount;
    private int _clockHandoffFireCount;

    /// <summary>
    /// Dev/test invariant guard. Throws <see cref="InvalidOperationException"/>
    /// when the load-bearing agreements (see docs/WIRE_CONTRACT.md) are violated:
    /// <list type="bullet">
    ///   <item>clock holder seat ≠ engine active player (CR 117 / 103.7), or</item>
    ///   <item>the wire phase is the raw, ambiguous "Main" (CR 505 — must be
    ///         split into PreCombatMain / PostCombatMain).</item>
    /// </list>
    /// The method is always present and unit-tested
    /// (<c>MatchFacadeBridgeTests.AssertAgreement_*</c>); it has no live call
    /// site — the phase-vocabulary invariant is enforced by the harness test
    /// <c>LayerAgreementInvariantTests.PhaseVocabulary_NeverRawMain</c> instead.
    /// </summary>
    public static void AssertAgreement(Guid activePlayerId, Guid clockHolderSeatId, string wirePhase)
    {
        if (clockHolderSeatId != activePlayerId)
        {
            throw new InvalidOperationException(
                $"clock holder ({clockHolderSeatId}) != engine active player " +
                $"({activePlayerId}) — the server clock must DERIVE its holder " +
                "from the engine (CR 117 / 103.7).");
        }

        if (wirePhase == "Main")
        {
            throw new InvalidOperationException(
                "wire phase \"Main\" must be disambiguated into PreCombatMain / " +
                "PostCombatMain before it reaches the wire (CR 505).");
        }
    }

    private async Task FireClockHandoffAsync(Guid matchId, string newHolderSub, string? expectedPrevHolderSub)
    {
        Interlocked.Increment(ref _clockHandoffFireCount);
        try
        {
            await _onActivePlayerChanged!(matchId, newHolderSub, expectedPrevHolderSub, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MatchFacadeBridge: clock-handoff callback faulted. " +
                "MatchId={MatchId} NewHolderSub={NewHolderSub} ExpectedPrev={ExpectedPrev}",
                matchId, newHolderSub, expectedPrevHolderSub);
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

        /// <summary>The live facade for this match — read for its current
        /// active player on each event so the clock holder can follow it.</summary>
        public GameFacade Facade { get; }

        /// <summary>Engine seat ids ↔ subs, captured at Attach.</summary>
        public PromptRouting Routing { get; }

        // Last active-player id we fired a clock handoff for. UNSEEDED (null)
        // until the first observed active player: the facade's active player
        // at Attach is always Alice (the facade exists before the engine
        // chooses who goes first), so seeding from it would mis-fire whenever
        // Bob goes first. Seeding lazily from the first observation captures
        // the engine's real first player and suppresses a turn-1 handoff (I2).
        // Guarded by _activePlayerGate.
        private readonly object _activePlayerGate = new();
        private Guid? _lastActivePlayerId;

        // The seat the bridge most recently handed the clock to (the new
        // active player of the last CLAIMED transition). Used by the prod
        // desync observer as the "clock holder the bridge believes it set"
        // — a divergence from the engine's live active player surfaces a
        // dropped/stale handoff (Slice 4b #3). Null until the first claimed
        // handoff. Read under no lock (single Guid, eventually-consistent
        // diagnostic) — a torn read just skips one observation.
        private Guid? _lastHandedOffSeat;
        public Guid? LastHandedOffSeat => _lastHandedOffSeat;

        public Attachment(
            IDisposable eventSub,
            IDisposable promptSub,
            GameFacade facade,
            PromptRouting routing)
        {
            _eventSub = eventSub;
            _promptSub = promptSub;
            Facade = facade;
            Routing = routing;
        }

        /// <summary>Atomically test-and-set the last-seen active player.
        /// Returns true (claiming a genuine transition) only when the tracker
        /// was already seeded AND <paramref name="current"/> differs from the
        /// last value — so a burst of events that all observe the same active
        /// player fires the handoff at most once, AND the very first
        /// observation only seeds (no handoff) so the engine's actual first
        /// player is captured rather than guessed (I2). On a claimed
        /// transition <paramref name="prevActivePlayer"/> is the seat we moved
        /// away from (used by the caller as the clock CAS expectation).</summary>
        public bool TryClaimActivePlayerChange(Guid current, out Guid prevActivePlayer)
        {
            lock (_activePlayerGate)
            {
                var prev = _lastActivePlayerId;
                _lastActivePlayerId = current;
                if (prev == null || prev.Value == current)
                {
                    // First observation (seed only) or no change → no handoff.
                    prevActivePlayer = current;
                    return false;
                }
                prevActivePlayer = prev.Value;
                // Record the seat we're handing the clock TO so the desync
                // observer can compare the bridge's believed holder against
                // the engine's live active player.
                _lastHandedOffSeat = current;
                return true;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _eventSub.Dispose(); } catch { /* swallow — disposing on teardown */ }
            try { _promptSub.Dispose(); } catch { /* swallow — disposing on teardown */ }
        }
    }
}
