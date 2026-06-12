using System.Collections.Concurrent;
using Majik.Core.Api.BotReplay;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// In-memory <see cref="IBotDecisionLogStore"/>. Used (a) as the default
/// registration so the flag-off path never reaches Mongo, and (b) in tests /
/// single-process deploys. Idempotent on (matchId, botSeq) via a per-match
/// dictionary keyed by botSeq — a duplicate append leaves the first entry
/// intact. Mirrors <see cref="InMemoryEngineCommandLogStore"/>.
/// </summary>
public class InMemoryBotDecisionLogStore : IBotDecisionLogStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<int, BotDecisionRecord>> _logs = new();

    public virtual Task AppendAsync(Guid matchId, BotDecisionRecord record, CancellationToken ct)
    {
        var log = _logs.GetOrAdd(matchId, _ => new ConcurrentDictionary<int, BotDecisionRecord>());
        // Idempotent: TryAdd no-ops when (matchId, botSeq) already present —
        // the first entry stands, mirroring the Mongo unique-index behaviour.
        log.TryAdd(record.BotSeq, record);
        return Task.CompletedTask;
    }

    public virtual Task<IReadOnlyList<BotDecisionRecord>> ReadAllAsync(Guid matchId, CancellationToken ct)
    {
        if (!_logs.TryGetValue(matchId, out var log))
            return Task.FromResult<IReadOnlyList<BotDecisionRecord>>(Array.Empty<BotDecisionRecord>());

        var result = log
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<BotDecisionRecord>>(result);
    }
}
