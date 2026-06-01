using Microsoft.Extensions.Hosting;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// PLAN 08 (body) — ensures the Mongo indexes for the durable command log
/// (unique on (matchId, seq) for idempotency) + the checkpoint store (unique on
/// matchId) at startup. Only registered when EnginePersistence:Enabled is true,
/// so the flag-off deploy never touches these collections.
/// </summary>
public sealed class EnginePersistenceIndexInitializer : IHostedService
{
    private readonly MongoEngineCommandLogStore _log;
    private readonly MongoEngineCheckpointStore _checkpoints;
    private readonly ILogger<EnginePersistenceIndexInitializer> _logger;

    public EnginePersistenceIndexInitializer(
        MongoEngineCommandLogStore log,
        MongoEngineCheckpointStore checkpoints,
        ILogger<EnginePersistenceIndexInitializer> logger)
    {
        _log = log;
        _checkpoints = checkpoints;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _log.EnsureIndexesAsync(ct);
            await _checkpoints.EnsureIndexesAsync(ct);
            _logger.LogInformation("Engine-persistence indexes ensured.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create engine-persistence indexes.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
