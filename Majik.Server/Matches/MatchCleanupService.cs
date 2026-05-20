using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace Majik.Server.Matches;

/// <summary>Periodic sweeper. Marks Open matches older than 1h as
/// Abandoned. Frees lobby slots from forgotten browsers.</summary>
public sealed class MatchCleanupService : BackgroundService
{
    private readonly MatchRepository _matches;
    private readonly IClock _clock;
    private readonly ILogger<MatchCleanupService> _log;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    public MatchCleanupService(MatchRepository matches, IClock clock, ILogger<MatchCleanupService> log)
    {
        _matches = matches;
        _clock = clock;
        _log = log;
    }

    public async Task RunSweepAsync(CancellationToken ct)
    {
        var cutoff = _clock.UtcNow - MaxAge;
        var open = await _matches.ListInStateAsync(MatchState.Open, ct);
        foreach (var m in open)
        {
            if (m.CreatedAt > cutoff) continue;
            var update = Builders<Match>.Update
                .Set(x => x.State, MatchState.Abandoned)
                .Set(x => x.UpdatedAt, _clock.UtcNow);
            await _matches.TryAtomicUpdateAsync(m.Id, MatchState.Open, update, ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RunSweepAsync(ct); }
            catch (Exception ex) { _log.LogError(ex, "MatchCleanup sweep failed"); }
            try { await Task.Delay(Interval, ct); } catch { return; }
        }
    }
}
