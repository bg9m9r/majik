using Majik.Core.Api;
using Majik.Core.Api.Commands;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// PLAN 08 (body) — durable, ordered, idempotent command log for one match's
/// engine. Every accepted command is appended at its monotonic seq so a
/// surviving / restarting replica can rehydrate the in-flight game by replaying
/// the log (combined with the latest checkpoint) instead of losing it.
///
/// <para><b>Idempotency:</b> <see cref="AppendAsync"/> is keyed on
/// <c>(matchId, seq)</c> with a unique index. A duplicate append of the same
/// <c>(matchId, seq)</c> is a NO-OP (last-writer-doesn't-win — the first entry
/// stands), so a forwarded-then-retried command, or a double-dispatch across a
/// claim handoff, can't corrupt the stream.</para>
/// </summary>
public interface IEngineCommandLogStore
{
    /// <summary>Append <paramref name="command"/> at <paramref name="seq"/> for
    /// <paramref name="matchId"/>. Idempotent on (matchId, seq): a second append
    /// of the same seq leaves the first entry intact and does not throw.</summary>
    Task AppendAsync(Guid matchId, long seq, DateTime at, GameCommand command, CancellationToken ct);

    /// <summary>Read every logged command for <paramref name="matchId"/> whose
    /// seq is strictly greater than <paramref name="afterSeq"/>, in ascending seq
    /// order. Pass -1 (or any value below the first seq) to read the whole log.</summary>
    Task<IReadOnlyList<LoggedCommand>> ReadSinceAsync(Guid matchId, long afterSeq, CancellationToken ct);

    /// <summary>The highest seq logged for <paramref name="matchId"/>, or -1 when
    /// the log is empty (no command has been accepted yet).</summary>
    Task<long> MaxSeqAsync(Guid matchId, CancellationToken ct);
}
