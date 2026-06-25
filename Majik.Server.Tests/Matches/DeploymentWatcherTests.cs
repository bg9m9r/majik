using FluentAssertions;
using Majik.Server.Composition;
using Majik.Server.Matches;
using Moq;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class DeploymentWatcherTests
{
    private static MatchReport Merged(string repo, DateTime mergedAt) => new()
    {
        Id = Guid.NewGuid(), ReporterSub = "alice", MatchId = Guid.NewGuid(),
        Title = "wedge", Status = ReportStatus.Merged, IssueNumber = 50, Repo = repo,
        FixMergedAt = mergedAt, CreatedAt = mergedAt, UpdatedAt = mergedAt,
    };

    private static Mock<MatchReportRepository> RepoWith(MatchReport report)
    {
        // Moq on the repo needs a real IMongoDatabase in the ctor (not null!).
        var reports = new Mock<MatchReportRepository>(MockBehavior.Loose,
            Mock.Of<MongoDB.Driver.IMongoDatabase>());
        reports.Setup(r => r.ListByStatusAsync(ReportStatus.Merged, It.IsAny<CancellationToken>()))
            .ReturnsAsync([report]);
        return reports;
    }

    [Fact]
    public async Task Sweep_delivers_core_report_when_api_started_after_merge_and_notifies()
    {
        var apiBootedAt = DateTime.UtcNow;
        var report = Merged("bg9m9r/majik", apiBootedAt.AddMinutes(-5)); // merged before boot ⇒ included
        var reports = RepoWith(report);
        var probe = new Mock<IPortalVersionProbe>();
        probe.Setup(p => p.GetBuildTimeAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DateTime?)null);
        var notif = new Mock<INotificationsPublisher>();
        var opts = new DeploymentOptions { CoreRepo = "bg9m9r/majik", PortalRepo = "bg9m9r/majik.portal" };

        var sweeper = new DeploymentSweeper(reports.Object, probe.Object, notif.Object, opts, apiBootedAt);
        await sweeper.SweepOnceAsync(default);

        reports.Verify(r => r.MarkDeliveredAsync(report.Id, It.IsAny<CancellationToken>()), Times.Once);
        notif.Verify(n => n.NotifyReportDeliveredAsync("alice", 50, "wedge", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sweep_skips_portal_report_until_portal_build_is_newer()
    {
        var report = Merged("bg9m9r/majik.portal", DateTime.UtcNow);
        var reports = RepoWith(report);
        var probe = new Mock<IPortalVersionProbe>();
        probe.Setup(p => p.GetBuildTimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(report.FixMergedAt!.Value.AddMinutes(-1)); // portal still on the OLD build
        var notif = new Mock<INotificationsPublisher>();
        var opts = new DeploymentOptions { CoreRepo = "bg9m9r/majik", PortalRepo = "bg9m9r/majik.portal" };

        var sweeper = new DeploymentSweeper(reports.Object, probe.Object, notif.Object, opts, DateTime.UtcNow);
        await sweeper.SweepOnceAsync(default);

        reports.Verify(r => r.MarkDeliveredAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        notif.Verify(n => n.NotifyReportDeliveredAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sweep_delivers_portal_report_once_portal_build_is_newer()
    {
        var mergedAt = DateTime.UtcNow.AddMinutes(-10);
        var report = Merged("bg9m9r/majik.portal", mergedAt);
        var reports = RepoWith(report);
        var probe = new Mock<IPortalVersionProbe>();
        probe.Setup(p => p.GetBuildTimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mergedAt.AddMinutes(5)); // portal redeployed AFTER the fix
        var notif = new Mock<INotificationsPublisher>();
        var opts = new DeploymentOptions { CoreRepo = "bg9m9r/majik", PortalRepo = "bg9m9r/majik.portal" };

        // API booted BEFORE the portal merge — proves the portal branch is what delivered.
        var sweeper = new DeploymentSweeper(reports.Object, probe.Object, notif.Object, opts, mergedAt.AddMinutes(-1));
        await sweeper.SweepOnceAsync(default);

        reports.Verify(r => r.MarkDeliveredAsync(report.Id, It.IsAny<CancellationToken>()), Times.Once);
        notif.Verify(n => n.NotifyReportDeliveredAsync("alice", 50, "wedge", It.IsAny<CancellationToken>()), Times.Once);
    }
}
