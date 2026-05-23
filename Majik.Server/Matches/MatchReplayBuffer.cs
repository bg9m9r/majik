using System.Collections.Concurrent;
using Majik.Bot.Diagnostics;
using Majik.Core.Api.Dtos;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Matches;

/// <summary>
/// In-memory, per-match capture of the ordered <see cref="EventDto"/> +
/// <see cref="BotDecision"/> stream that crosses the engine→hub seam.
/// Powers the <c>GET /matches/{id}/replay</c> endpoint: a minimal
/// "share a finished game" feature that lets a player download the
/// full event stream as JSON after the match ends.
///
/// <para>This is a side-channel — capture is wired in addition to the
/// existing SignalR fan-out, NOT in place of it. A capture fault (full
/// buffer, dropped record) must not perturb the live broadcast.</para>
///
/// <para><b>Storage strategy (MVP).</b> Everything lives in this
/// singleton. We do NOT persist to Mongo: replay buffers are intended
/// for "I just finished a game, let me grab the log" — not durable
/// history. Buffers survive process restart only if rebuilt by future
/// work. The lifecycle is:
/// <list type="number">
///   <item><description>Match starts → buffer is implicitly created on
///   first <see cref="RecordEvent"/> or <see cref="RecordDecision"/>
///   call.</description></item>
///   <item><description>Match ends (Detach via concede / abandon /
///   timeout / completion) → buffer is sealed: no new entries are
///   accepted, but it remains downloadable.</description></item>
///   <item><description>Eviction → finished-match buffers are LRU-evicted
///   when the count of retained matches exceeds
///   <see cref="MaxRetainedMatches"/>. Active (un-sealed) buffers are
///   never evicted: they belong to a live game.</description></item>
/// </list></para>
///
/// <para><b>Per-match cap.</b> Each buffer accepts up to
/// <see cref="MaxEntriesPerMatch"/> entries. Beyond that, additional
/// records are dropped and an overflow flag is set on the dto so the
/// downloader knows the stream was truncated. Typical bot games we've
/// observed fall well below this cap (low thousands of entries); the
/// cap exists as a runaway-event guardrail, not as a normal expectation.
/// If real games routinely exceed it, the right next step is persistence
/// + streaming download, not a bigger in-memory ring.</para>
/// </summary>
public sealed class MatchReplayBuffer
{
    /// <summary>Per-match cap. See class docs.</summary>
    public const int MaxEntriesPerMatch = 20_000;

    /// <summary>Cap on the number of finished-match buffers retained.
    /// Active matches are exempt — only sealed buffers participate in
    /// LRU eviction so a live game is never accidentally evicted.</summary>
    public const int MaxRetainedMatches = 200;

    private readonly IClock _clock;
    private readonly ILogger<MatchReplayBuffer>? _logger;
    private readonly ConcurrentDictionary<Guid, Buffer> _buffers = new();
    private long _seqCounter;

    public MatchReplayBuffer(IClock clock, ILogger<MatchReplayBuffer>? logger = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
    }

    /// <summary>Test hook — number of buffers currently retained
    /// (active + sealed).</summary>
    internal int BufferCount => _buffers.Count;

    /// <summary>Capture an engine event for <paramref name="matchId"/>.
    /// Safe to call on a sealed buffer (no-op).</summary>
    public void RecordEvent(Guid matchId, EventDto evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        TryAppend(matchId, ReplayEntry.ForEvent(NextSeq(), _clock.UtcNow, evt));
    }

    /// <summary>Capture a bot decision for <paramref name="matchId"/>.
    /// Safe to call on a sealed buffer (no-op).</summary>
    public void RecordDecision(Guid matchId, BotDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        TryAppend(matchId, ReplayEntry.ForDecision(NextSeq(), _clock.UtcNow, decision));
    }

    /// <summary>
    /// Mark the buffer for <paramref name="matchId"/> as terminal: no
    /// further entries are accepted. Called from
    /// <see cref="MatchFacadeBridge.Detach"/> so the engine→hub teardown
    /// also closes the replay log. After sealing, the buffer is eligible
    /// for LRU eviction once <see cref="MaxRetainedMatches"/> sealed
    /// buffers are retained.
    /// </summary>
    public void Seal(Guid matchId)
    {
        if (_buffers.TryGetValue(matchId, out var buf))
        {
            buf.Seal(_clock.UtcNow);
            EvictIfOver();
        }
    }

    /// <summary>
    /// Snapshot the current entries for <paramref name="matchId"/>.
    /// Returns <c>null</c> if no buffer exists (match never recorded
    /// anything, or was evicted). Returns a stable copy so concurrent
    /// recording can't mutate the result.
    /// </summary>
    public MatchReplayDto? GetReplay(Guid matchId)
    {
        if (!_buffers.TryGetValue(matchId, out var buf))
        {
            return null;
        }
        return buf.Snapshot(matchId);
    }

    private void TryAppend(Guid matchId, ReplayEntry entry)
    {
        var buf = _buffers.GetOrAdd(matchId, _ => new Buffer());
        try
        {
            buf.Append(entry);
        }
        catch (Exception ex)
        {
            // Defensive: a capture-side fault must never bubble out of
            // the engine→hub bridge. Log and drop.
            _logger?.LogError(ex,
                "MatchReplayBuffer: failed to append entry. MatchId={MatchId} Kind={Kind}",
                matchId, entry.Kind);
        }
    }

    private long NextSeq() => Interlocked.Increment(ref _seqCounter);

    private void EvictIfOver()
    {
        // Sealed buffers compete for the retention slots. Take a snapshot
        // of sealed buffers, sort by sealed-at ascending, drop the oldest
        // until we're under the cap. Active buffers are excluded so live
        // matches always have a place to write.
        var sealedBufs = _buffers
            .Where(kv => kv.Value.SealedAt.HasValue)
            .OrderBy(kv => kv.Value.SealedAt!.Value)
            .ToList();
        var over = sealedBufs.Count - MaxRetainedMatches;
        for (int i = 0; i < over; i++)
        {
            var victim = sealedBufs[i].Key;
            _buffers.TryRemove(victim, out _);
        }
    }

    /// <summary>One in-memory ring per match. Thread-safe via lock — the
    /// throughput is low enough (engine event rate, not log-aggregator
    /// rate) that a single mutex is the right level of complexity.</summary>
    private sealed class Buffer
    {
        private readonly object _gate = new();
        private readonly List<ReplayEntry> _entries = new();
        private bool _overflowed;
        public DateTime? SealedAt { get; private set; }

        public void Append(ReplayEntry entry)
        {
            lock (_gate)
            {
                if (SealedAt.HasValue) return; // ignore writes after seal
                if (_entries.Count >= MaxEntriesPerMatch)
                {
                    _overflowed = true;
                    return;
                }
                _entries.Add(entry);
            }
        }

        public void Seal(DateTime at)
        {
            lock (_gate)
            {
                if (!SealedAt.HasValue) SealedAt = at;
            }
        }

        public MatchReplayDto Snapshot(Guid matchId)
        {
            lock (_gate)
            {
                // Defensive copy: callers can iterate without taking the lock.
                return new MatchReplayDto(
                    MatchId: matchId,
                    SealedAt: SealedAt,
                    Truncated: _overflowed,
                    EntryCount: _entries.Count,
                    Entries: _entries.ToArray());
            }
        }
    }
}
