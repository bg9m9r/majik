using Majik.Core.Api;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// PLAN 08 (body) — a durable engine checkpoint for one match: the
/// <see cref="GameFacade.SaveSnapshot"/> taken after the command at
/// <see cref="LastAppliedSeq"/> was applied. The snapshot bundles the command
/// PREFIX (the log up to <see cref="LastAppliedSeq"/>) + the game seed, so
/// rehydration replays only the commands logged AFTER the checkpoint rather than
/// the whole history — bounding replay latency.
/// </summary>
public sealed record EngineCheckpoint(
    Guid MatchId,
    long LastAppliedSeq,
    int Seed,
    GameSnapshot Snapshot,
    DateTime At);

/// <summary>
/// Durable store for the latest per-match <see cref="EngineCheckpoint"/>. Only
/// the most recent checkpoint is needed for rehydration; implementations MAY
/// retain history but the contract is "give me the latest".
/// </summary>
public interface IEngineCheckpointStore
{
    /// <summary>Persist <paramref name="checkpoint"/> as the latest checkpoint
    /// for its match. A later checkpoint (higher LastAppliedSeq) supersedes an
    /// earlier one.</summary>
    Task SaveAsync(EngineCheckpoint checkpoint, CancellationToken ct);

    /// <summary>The latest checkpoint for <paramref name="matchId"/>, or null
    /// when none has been written yet (rehydration then replays from seq 0).</summary>
    Task<EngineCheckpoint?> GetLatestAsync(Guid matchId, CancellationToken ct);
}
