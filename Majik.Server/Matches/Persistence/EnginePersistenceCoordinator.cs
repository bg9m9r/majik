using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Game;
using Majik.Core.Random;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// PLAN 08 (body) — the durable-persistence orchestrator, wholly gated behind
/// <see cref="EnginePersistenceOptions.Enabled"/> (DEFAULT OFF). It is the single
/// place that:
/// <list type="bullet">
/// <item>appends every accepted command to the durable <see cref="IEngineCommandLogStore"/>
///   at its facade seq (idempotent on (matchId, seq));</item>
/// <item>periodically writes a <see cref="IEngineCheckpointStore"/> checkpoint
///   (<see cref="GameFacade.SaveSnapshot"/> + last applied seq) so rehydration
///   replays only commands since the checkpoint;</item>
/// <item>on a registry MISS, rebuilds the in-flight game via
///   <see cref="GameFacade.Rehydrate"/> from the latest checkpoint + the commands
///   logged since (or the whole log when no checkpoint exists).</item>
/// </list>
///
/// <para>With the flag OFF every method is a no-op / returns null, so the server
/// behaves exactly as today (no durable writes, in-process only) and the deploy
/// posture is unchanged.</para>
///
/// <para>This coordinator does NOT decide WHEN to rehydrate (that is the
/// claim-serialized command entry's job, so only the claim winner rebuilds);
/// it just performs a rehydrate when asked and hands back a LIVE facade for the
/// caller to register + bridge-attach.</para>
/// </summary>
public sealed class EnginePersistenceCoordinator
{
    private readonly IEngineCommandLogStore _log;
    private readonly IEngineCheckpointStore _checkpoints;
    private readonly EnginePersistenceOptions _options;
    private readonly Func<DateTime> _clock;
    private readonly ILogger<EnginePersistenceCoordinator>? _logger;

    public EnginePersistenceCoordinator(
        IEngineCommandLogStore log,
        IEngineCheckpointStore checkpoints,
        IOptions<EnginePersistenceOptions> options,
        Func<DateTime>? clock = null,
        ILogger<EnginePersistenceCoordinator>? logger = null)
    {
        _log = log;
        _checkpoints = checkpoints;
        _options = options.Value;
        _clock = clock ?? (() => DateTime.UtcNow);
        _logger = logger;
    }

    /// <summary>True when durable persistence is switched on. Callers short-
    /// circuit the whole feature when false so the flag-off path is exactly
    /// today's behaviour.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>
    /// Durably record an accepted command. Appends <paramref name="command"/> at
    /// its facade-assigned <paramref name="seq"/> (idempotent on (matchId, seq)),
    /// then takes a checkpoint when the seq crosses the configured cadence. A
    /// checkpoint-write failure is swallowed (logged): the durable command log is
    /// the canonical source, so a missing checkpoint just means rehydration falls
    /// back to a longer (full-log) replay — never data loss.
    /// </summary>
    public async Task RecordCommandAsync(
        Guid matchId, GameFacade facade, long seq, GameCommand command, CancellationToken ct)
    {
        if (!_options.Enabled) return;

        await _log.AppendAsync(matchId, seq, _clock(), command, ct);

        if (ShouldCheckpoint(seq))
        {
            try
            {
                var snapshot = facade.SaveSnapshot();
                var checkpoint = new EngineCheckpoint(matchId, seq, snapshot.Seed, snapshot, _clock());
                await _checkpoints.SaveAsync(checkpoint, ct);
            }
            catch (Exception ex)
            {
                // Checkpoint is an optimization, not the source of truth — fall
                // back to full-log replay rather than failing the command.
                _logger?.LogWarning(ex,
                    "Engine checkpoint write failed; rehydration will fall back to " +
                    "full-log replay. MatchId={MatchId} Seq={Seq}", matchId, seq);
            }
        }
    }

    private bool ShouldCheckpoint(long seq)
    {
        var every = _options.CheckpointEveryCommands;
        if (every <= 0) return false;
        // seq is 1-based and strictly monotonic; checkpoint on each multiple.
        return seq % every == 0;
    }

    /// <summary>
    /// Rebuild a LIVE facade for an in-flight match that is no longer in the
    /// in-process registry, by replaying the durable log (combined with the
    /// latest checkpoint) under a seed-scope so the result is id-identical to the
    /// crashed original. Returns null when persistence is off, or when the match
    /// has no durable log at all (never started / nothing to rehydrate).
    ///
    /// <para>The caller has already won the ownership claim (the SETNX
    /// serialization point), so only ONE replica reaches here — no split-brain.</para>
    /// </summary>
    /// <param name="buildFreshFacade">Reconstructs a fresh facade with the SAME
    /// initial board/decks the original started from (decks materialized from the
    /// persisted seed + deck snapshot). Invoked under the id-scope inside
    /// <see cref="GameFacade.Rehydrate"/>.</param>
    public async Task<GameFacade?> TryRehydrateAsync(
        Guid matchId,
        int seed,
        Func<GameFacade> buildFreshFacade,
        CancellationToken ct)
    {
        if (!_options.Enabled) return null;

        var maxSeq = await _log.MaxSeqAsync(matchId, ct);
        if (maxSeq < 0)
        {
            // No durable command log → nothing to rehydrate.
            return null;
        }

        var checkpoint = await _checkpoints.GetLatestAsync(matchId, ct);
        IReadOnlyList<LoggedCommand> replay;

        if (checkpoint != null)
        {
            // checkpoint.Snapshot.Log is the command PREFIX [0..LastAppliedSeq];
            // append the commands logged AFTER the checkpoint. Concatenating the
            // two reconstructs the full ordered log, so the rehydrate is
            // equivalent to a full replay from 0 — just cheaper to fetch.
            var since = await _log.ReadSinceAsync(matchId, checkpoint.LastAppliedSeq, ct);
            replay = checkpoint.Snapshot.Log.Concat(since).ToList();
            _logger?.LogInformation(
                "Rehydrating from checkpoint. MatchId={MatchId} CheckpointSeq={Seq} " +
                "PrefixCount={Prefix} SinceCount={Since}",
                matchId, checkpoint.LastAppliedSeq, checkpoint.Snapshot.Log.Count, since.Count);
        }
        else
        {
            // No checkpoint — replay the whole durable log from the start.
            replay = await _log.ReadSinceAsync(matchId, -1, ct);
            _logger?.LogInformation(
                "Rehydrating from full log (no checkpoint). MatchId={MatchId} Count={Count}",
                matchId, replay.Count);
        }

        var facade = await GameFacade.Rehydrate(buildFreshFacade, seed, replay, ct: ct);
        return facade;
    }
}
