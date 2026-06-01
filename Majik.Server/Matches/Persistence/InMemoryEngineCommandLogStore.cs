using System.Collections.Concurrent;
using Majik.Core.Api;
using Majik.Core.Api.Commands;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// In-memory <see cref="IEngineCommandLogStore"/>. Used (a) as the default
/// registration so the flag-off path never reaches Mongo, and (b) in tests /
/// single-process deploys. Idempotent on (matchId, seq) via a per-match
/// dictionary keyed by seq — a duplicate append leaves the first entry intact.
///
/// <para>Not durable across a process restart; the Mongo implementation is the
/// production durable store when the feature flag is on AND Mongo is wired.</para>
/// </summary>
public class InMemoryEngineCommandLogStore : IEngineCommandLogStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<long, LoggedCommand>> _logs = new();

    public virtual Task AppendAsync(
        Guid matchId, long seq, DateTime at, GameCommand command, CancellationToken ct)
    {
        var log = _logs.GetOrAdd(matchId, _ => new ConcurrentDictionary<long, LoggedCommand>());
        // Idempotent: TryAdd no-ops when (matchId, seq) already present — the
        // first entry stands, mirroring the Mongo unique-index upsert.
        log.TryAdd(seq, new LoggedCommand(at, command));
        return Task.CompletedTask;
    }

    public virtual Task<IReadOnlyList<LoggedCommand>> ReadSinceAsync(
        Guid matchId, long afterSeq, CancellationToken ct)
    {
        if (!_logs.TryGetValue(matchId, out var log))
            return Task.FromResult<IReadOnlyList<LoggedCommand>>(Array.Empty<LoggedCommand>());

        var result = log
            .Where(kv => kv.Key > afterSeq)
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<LoggedCommand>>(result);
    }

    public virtual Task<long> MaxSeqAsync(Guid matchId, CancellationToken ct)
    {
        if (!_logs.TryGetValue(matchId, out var log) || log.IsEmpty)
            return Task.FromResult(-1L);
        return Task.FromResult(log.Keys.Max());
    }
}
