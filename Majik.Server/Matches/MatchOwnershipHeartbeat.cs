namespace Majik.Server.Matches;

/// <summary>
/// Hosted service that refreshes the Redis TTL on every match this
/// instance currently owns. Runs on a fixed interval well below the
/// owner TTL (<see cref="MatchOwnership.OwnerTtl"/>) so a healthy owner
/// never loses its claim.
///
/// If a refresh comes back false (ownership lost — typically the TTL
/// expired during a stall and another instance grabbed it), the match is
/// dropped from the local owned-set. PR 5 stops there; PR 6 will hook
/// this so the local <c>GameFacade</c> is torn down and clients are told
/// to reconnect.
/// </summary>
public sealed class MatchOwnershipHeartbeat : BackgroundService
{
    /// <summary>Heartbeat interval. Half the TTL — survives a single missed beat.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IMatchOwnership _ownership;
    private readonly ILogger<MatchOwnershipHeartbeat> _logger;

    public MatchOwnershipHeartbeat(IMatchOwnership ownership, ILogger<MatchOwnershipHeartbeat> logger)
    {
        _ownership = ownership;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var matchId in _ownership.Owned)
                {
                    var stillMine = await _ownership.RefreshAsync(matchId, stoppingToken);
                    if (!stillMine)
                    {
                        _logger.LogWarning("Lost ownership of match {MatchId} (TTL expired)", matchId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Heartbeat failures are recoverable — log and keep going.
                // If Redis is unreachable for long enough, ownership keys
                // will expire and other replicas can take over.
                _logger.LogError(ex, "Match ownership heartbeat failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
