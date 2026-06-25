using FluentAssertions;
using Majik.Server.Composition;
using Majik.Server.Matches;
using Moq;
using Xunit;
using Match = Majik.Server.Matches.Match;

namespace Majik.Server.Tests.Matches;

public class IssueReportServiceTests
{
    private static ReportingOptions Allow(string sub) =>
        new() { Enabled = true, TrustedTesterSubs = { sub }, MaxReportsPerHour = 5, ReplayCap = 300 };

    // Real Match shape (Majik.Server/Matches/Match.cs): the seat type is
    // MatchPlayer (not "Seat"); Match requires Visibility/Format/ClockMinutes/
    // CreatedAt in addition to the plan's listed fields.
    private static Match PlayingMatch(Guid id, string creatorSub) => new()
    {
        Id = id, State = MatchState.Playing,
        Visibility = MatchVisibility.Public, Format = "constructed", ClockMinutes = 20,
        Creator = new MatchPlayer { Sub = creatorSub, Handle = "Alice", DeckId = "deck-a", DeckSnapshot = new() },
        Opponent = new MatchPlayer { Sub = "bot:burn", Handle = "Bot", DeckId = "deck-b", DeckSnapshot = new() },
        GameId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task NonAllowlisted_sub_is_rejected()
    {
        var matches = new Mock<MatchRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        var reports = new Mock<MatchReportRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        var gh = new Mock<IGitHubIssueClient>();
        var svc = new IssueReportService(matches.Object, reports.Object, gh.Object,
            gameFactory: null, replayBuffer: null, Allow("alice"));

        var r = await svc.CreateAsync("mallory", Guid.NewGuid(),
            new ReportIssueRequest("bug", null), default);

        r.IsSuccess.Should().BeFalse();
        r.Failure.Should().Be(ReportFailure.NotAllowlisted);
        gh.Verify(x => x.CreateIssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Allowlisted_participant_creates_issue_and_persists_record()
    {
        var matchId = Guid.NewGuid();
        var match = PlayingMatch(matchId, "alice");
        var matches = new Mock<MatchRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        matches.Setup(m => m.GetByIdAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        var reports = new Mock<MatchReportRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        reports.Setup(r => r.CountBySubSinceAsync("alice", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        MatchReport? saved = null;
        reports.Setup(r => r.InsertAsync(It.IsAny<MatchReport>(), It.IsAny<CancellationToken>()))
            .Callback<MatchReport, CancellationToken>((mr, _) => saved = mr).Returns(Task.CompletedTask);
        var gh = new Mock<IGitHubIssueClient>();
        gh.Setup(x => x.CreateIssueAsync(It.IsAny<string>(), It.IsAny<string>(), "app-report", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatedIssue(7, "https://github.com/o/r/issues/7"));

        // gameFactory + replayBuffer null → bundle assembly degrades gracefully (state/replay omitted).
        var svc = new IssueReportService(matches.Object, reports.Object, gh.Object,
            gameFactory: null, replayBuffer: null, Allow("alice"));

        var r = await svc.CreateAsync("alice", matchId, new ReportIssueRequest("Boltwave wedge", null), default);

        r.IsSuccess.Should().BeTrue();
        r.Value!.IssueNumber.Should().Be(7);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(ReportStatus.IssueOpen);
        saved.IssueNumber.Should().Be(7);
        saved.ReporterSub.Should().Be("alice");
    }

    [Fact]
    public async Task CanReport_true_for_allowlisted_participant()
    {
        var matchId = Guid.NewGuid();
        var matches = new Mock<MatchRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        matches.Setup(m => m.GetByIdAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(PlayingMatch(matchId, "alice"));
        var reports = new Mock<MatchReportRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        var svc = new IssueReportService(matches.Object, reports.Object, new Mock<IGitHubIssueClient>().Object,
            null, null, Allow("alice"));

        (await svc.CanReportAsync("alice", matchId, default)).Should().BeTrue();
    }

    [Fact]
    public async Task CanReport_false_when_not_allowlisted_or_not_participant()
    {
        var matchId = Guid.NewGuid();
        var matches = new Mock<MatchRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        matches.Setup(m => m.GetByIdAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(PlayingMatch(matchId, "alice"));
        var reports = new Mock<MatchReportRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        var svc = new IssueReportService(matches.Object, reports.Object, new Mock<IGitHubIssueClient>().Object,
            null, null, Allow("alice"));

        // not allowlisted (mallory not on the list)
        (await svc.CanReportAsync("mallory", matchId, default)).Should().BeFalse();
        // allowlisted but NOT seated in this match
        var svc2 = new IssueReportService(matches.Object, reports.Object, new Mock<IGitHubIssueClient>().Object,
            null, null, Allow("carol"));
        (await svc2.CanReportAsync("carol", matchId, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Over_rate_limit_is_rejected()
    {
        var matchId = Guid.NewGuid();
        var match = PlayingMatch(matchId, "alice");
        var matches = new Mock<MatchRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        matches.Setup(m => m.GetByIdAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        var reports = new Mock<MatchReportRepository>(MockBehavior.Loose, Mock.Of<MongoDB.Driver.IMongoDatabase>());
        reports.Setup(r => r.CountBySubSinceAsync("alice", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(5);
        var svc = new IssueReportService(matches.Object, reports.Object, new Mock<IGitHubIssueClient>().Object,
            null, null, Allow("alice"));

        var r = await svc.CreateAsync("alice", matchId, new ReportIssueRequest("bug", null), default);

        r.Failure.Should().Be(ReportFailure.RateLimited);
    }

    [Fact]
    public void FitWithinLimit_picks_first_candidate_that_fits()
    {
        var candidates = new[] { new string('a', 100), new string('b', 40), new string('c', 10) };
        // max 50 → the 100-char one is too big, the 40-char one fits first.
        IssueReportService.FitWithinLimit(candidates, 50).Should().Be(new string('b', 40));
    }

    [Fact]
    public void FitWithinLimit_hard_truncates_when_none_fit()
    {
        var candidates = new[] { new string('a', 100), new string('b', 80) };
        var result = IssueReportService.FitWithinLimit(candidates, 50);
        result.Length.Should().Be(50);
        result.Should().Be(new string('b', 50)); // smallest, hard-truncated to the cap
    }

    [Fact]
    public void FitWithinLimit_is_lazy_stops_at_first_fit()
    {
        var evaluated = 0;
        IEnumerable<string> Gen()
        {
            evaluated++; yield return new string('x', 10);   // fits immediately
            evaluated++; yield return new string('y', 9999);
        }
        IssueReportService.FitWithinLimit(Gen(), 50).Length.Should().Be(10);
        evaluated.Should().Be(1); // never composed the 2nd (expensive) candidate
    }
}
