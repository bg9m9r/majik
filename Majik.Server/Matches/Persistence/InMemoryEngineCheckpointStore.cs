using System.Collections.Concurrent;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// In-memory <see cref="IEngineCheckpointStore"/>. Keeps only the latest
/// checkpoint per match (a later, higher-seq checkpoint replaces an earlier
/// one). Used as the default registration + in tests; the Mongo implementation
/// is the production durable store when the flag is on AND Mongo is wired.
/// </summary>
public class InMemoryEngineCheckpointStore : IEngineCheckpointStore
{
    private readonly ConcurrentDictionary<Guid, EngineCheckpoint> _latest = new();

    public virtual Task SaveAsync(EngineCheckpoint checkpoint, CancellationToken ct)
    {
        _latest.AddOrUpdate(
            checkpoint.MatchId,
            checkpoint,
            // Keep whichever checkpoint reflects more applied commands.
            (_, existing) => checkpoint.LastAppliedSeq >= existing.LastAppliedSeq ? checkpoint : existing);
        return Task.CompletedTask;
    }

    public virtual Task<EngineCheckpoint?> GetLatestAsync(Guid matchId, CancellationToken ct)
        => Task.FromResult(_latest.TryGetValue(matchId, out var c) ? c : null);
}
