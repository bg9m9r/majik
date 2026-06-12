using Majik.Core.Api.BotReplay;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// Durable, ordered, idempotent log of one match's BOT answers (parallel to
/// <see cref="IEngineCommandLogStore"/>, which logs the HUMAN commands). Every
/// answer the bot seat gives in-engine is appended at its per-match monotonic
/// <c>botSeq</c> so a rehydrating replica can replay the answers VERBATIM via
/// <see cref="Majik.Core.Api.BotReplay.ScriptedPlayerAgent"/> — no recompute,
/// so wall-clock-nondeterministic search (MCTS) survives restarts.
///
/// <para><b>Idempotency:</b> <see cref="AppendAsync"/> is keyed on
/// <c>(matchId, botSeq)</c> with a unique index. A duplicate append is a NO-OP
/// (the first entry stands), so a retried append across a claim handoff can't
/// corrupt the stream.</para>
/// </summary>
public interface IBotDecisionLogStore
{
    /// <summary>Append <paramref name="record"/> for <paramref name="matchId"/>.
    /// Idempotent on (matchId, record.BotSeq): a second append of the same
    /// botSeq leaves the first entry intact and does not throw.</summary>
    Task AppendAsync(Guid matchId, BotDecisionRecord record, CancellationToken ct);

    /// <summary>Read the whole recorded decision stream for
    /// <paramref name="matchId"/> in ascending botSeq order (replay is always
    /// full-log-from-start — checkpoints only bundle the command prefix).</summary>
    Task<IReadOnlyList<BotDecisionRecord>> ReadAllAsync(Guid matchId, CancellationToken ct);
}
