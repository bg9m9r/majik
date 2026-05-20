using Microsoft.Extensions.Hosting;

namespace Majik.Server.Decks;

public sealed class DeckIndexInitializer : IHostedService
{
    private readonly DeckRepository _repo;
    private readonly ILogger<DeckIndexInitializer> _log;

    public DeckIndexInitializer(DeckRepository repo, ILogger<DeckIndexInitializer> log)
    {
        _repo = repo;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _repo.EnsureIndexesAsync(ct);
            _log.LogInformation("Deck indexes ensured.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to create Deck indexes.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
