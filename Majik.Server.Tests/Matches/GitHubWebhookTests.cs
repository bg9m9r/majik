using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Majik.Server.Matches;
using Moq;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class GitHubWebhookTests
{
    private static string Sign(string body, string secret)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Verify_accepts_correct_signature_rejects_wrong()
    {
        var body = "{\"x\":1}";
        GitHubWebhookVerifier.IsValid(body, Sign(body, "s3cr3t"), "s3cr3t").Should().BeTrue();
        GitHubWebhookVerifier.IsValid(body, Sign(body, "wrong"), "s3cr3t").Should().BeFalse();
        GitHubWebhookVerifier.IsValid(body, null, "s3cr3t").Should().BeFalse();
    }

    [Fact]
    public void ParseClosedIssueNumbers_finds_same_and_cross_repo_refs()
    {
        var prBody = "Fixes the wedge.\n\nCloses #50\nAlso closes bg9m9r/majik#51";
        var nums = GitHubWebhookService.ParseClosedIssueNumbers(prBody, "bg9m9r/majik");
        nums.Should().BeEquivalentTo(new[] { 50, 51 });
    }

    [Fact]
    public void ParseClosedIssueNumbers_ignores_other_repo_cross_refs()
    {
        var prBody = "Closes other/repo#99\nCloses #7";
        var nums = GitHubWebhookService.ParseClosedIssueNumbers(prBody, "bg9m9r/majik");
        nums.Should().BeEquivalentTo(new[] { 7 });
    }

    [Fact]
    public async Task HandlePullRequest_merged_closing_app_report_marks_merged()
    {
        var report = new MatchReport
        {
            Id = Guid.NewGuid(), ReporterSub = "alice", MatchId = Guid.NewGuid(),
            Title = "wedge", Status = ReportStatus.IssueOpen, IssueNumber = 50,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var reports = new Mock<MatchReportRepository>(MockBehavior.Loose,
            Mock.Of<MongoDB.Driver.IMongoDatabase>());
        reports.Setup(r => r.GetByIssueNumberAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var svc = new GitHubWebhookService(reports.Object);
        var json = """
        {
          "action": "closed",
          "pull_request": { "merged": true, "body": "Closes #50", "merged_at": "2026-06-24T10:00:00Z" },
          "repository": { "full_name": "bg9m9r/majik" }
        }
        """;

        var handled = await svc.HandlePullRequestAsync(json, "bg9m9r/majik", default);

        handled.Should().BeTrue();
        reports.Verify(r => r.MarkMergedAsync(report.Id, "bg9m9r/majik",
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandlePullRequest_closed_but_not_merged_is_noop()
    {
        var reports = new Mock<MatchReportRepository>(MockBehavior.Loose,
            Mock.Of<MongoDB.Driver.IMongoDatabase>());
        var svc = new GitHubWebhookService(reports.Object);
        var json = """
        { "action": "closed", "pull_request": { "merged": false, "body": "Closes #50" },
          "repository": { "full_name": "bg9m9r/majik" } }
        """;

        var handled = await svc.HandlePullRequestAsync(json, "bg9m9r/majik", default);

        handled.Should().BeFalse();
        reports.Verify(r => r.MarkMergedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
