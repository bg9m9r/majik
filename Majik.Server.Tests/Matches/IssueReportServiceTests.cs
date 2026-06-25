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
}
