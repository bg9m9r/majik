using Majik.Server.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Majik.Server.Matches;

/// <summary>Pure sweep: flip Merged→Delivered when the affected service's live
/// build is newer than the fix merge, then notify the reporter.</summary>
public sealed class DeploymentSweeper
{
    private readonly MatchReportRepository _reports;
    private readonly IPortalVersionProbe _portal;
    private readonly INotificationsPublisher _notif;
    private readonly DeploymentOptions _opts;
    private readonly DateTime _apiBootedAt;

    public DeploymentSweeper(MatchReportRepository reports, IPortalVersionProbe portal,
        INotificationsPublisher notif, DeploymentOptions opts, DateTime apiBootedAt)
    {
        _reports = reports;
        _portal = portal;
        _notif = notif;
        _opts = opts;
        _apiBootedAt = apiBootedAt;
    }

    public async Task SweepOnceAsync(CancellationToken ct)
    {
        var merged = await _reports.ListByStatusAsync(ReportStatus.Merged, ct);
        if (merged.Count == 0) return;
        var portalBuildTime = await _portal.GetBuildTimeAsync(ct);

        foreach (var r in merged)
        {
            if (r.FixMergedAt is not DateTime mergedAt) continue;
            var delivered = r.Repo == _opts.PortalRepo
                ? portalBuildTime is DateTime pbt && pbt > mergedAt   // portal: live build newer
                : _apiBootedAt > mergedAt;                            // core/API: this process booted after merge
            if (!delivered) continue;
            await _reports.MarkDeliveredAsync(r.Id, ct);
            await _notif.NotifyReportDeliveredAsync(r.ReporterSub, r.IssueNumber ?? 0, r.Title, ct);
        }
    }
}

/// <summary>Background timer shell around <see cref="DeploymentSweeper"/>.
/// <see cref="BootedAt"/> is captured once at process start ≈ the API deploy
/// time (a manual deploy off latest main after the fix merged ⇒ BootedAt &gt;
/// FixMergedAt ⇒ the running API contains the fix). The on-boot first sweep
/// handles the API-fix case immediately; the poll loop handles portal fixes as
/// the portal redeploys.</summary>
public sealed class DeploymentWatcher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly DeploymentOptions _opts;
    private readonly IPortalVersionProbe _portal;
    private readonly INotificationsPublisher _notif;
    private readonly ILogger<DeploymentWatcher> _log;
    private static readonly DateTime BootedAt = DateTime.UtcNow;

    public DeploymentWatcher(IServiceProvider sp, DeploymentOptions opts,
        IPortalVersionProbe portal, INotificationsPublisher notif, ILogger<DeploymentWatcher> log)
    {
        _sp = sp;
        _opts = opts;
        _portal = portal;
        _notif = notif;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var reports = scope.ServiceProvider.GetRequiredService<MatchReportRepository>();
                var sweeper = new DeploymentSweeper(reports, _portal, _notif, _opts, BootedAt);
                await sweeper.SweepOnceAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DeploymentWatcher sweep failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(_opts.PollSeconds), ct); }
            catch { return; }
        }
    }
}
