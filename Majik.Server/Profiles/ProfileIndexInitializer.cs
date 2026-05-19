using Microsoft.Extensions.Hosting;

namespace Majik.Server.Profiles;

/// <summary>Runs at startup. Idempotent — creates the unique indexes on
/// <c>userProfiles</c> (sub, handle) if they don't already exist.
/// Registered only when Mongo is configured.</summary>
public sealed class ProfileIndexInitializer : IHostedService
{
    private readonly UserProfileRepository _repo;
    private readonly ILogger<ProfileIndexInitializer> _log;

    public ProfileIndexInitializer(
        UserProfileRepository repo,
        ILogger<ProfileIndexInitializer> log)
    {
        _repo = repo;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _repo.EnsureIndexesAsync(ct);
            _log.LogInformation("UserProfile indexes ensured.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to create UserProfile indexes.");
            // Don't crash startup — endpoints will surface 503 on call.
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
