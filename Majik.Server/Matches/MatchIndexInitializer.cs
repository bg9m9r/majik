using Microsoft.Extensions.Hosting;

namespace Majik.Server.Matches;

public sealed class MatchIndexInitializer : IHostedService
{
    private readonly MatchRepository _repo;
    private readonly ILogger<MatchIndexInitializer> _log;

    public MatchIndexInitializer(MatchRepository repo, ILogger<MatchIndexInitializer> log)
    {
        _repo = repo;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _repo.EnsureIndexesAsync(ct);
            _log.LogInformation("Match indexes ensured.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to create Match indexes.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
