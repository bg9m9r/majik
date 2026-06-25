using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Tests.Profiles;
using MongoDB.Driver;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchReportRepositoryTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchReportRepositoryTests(TestMongoFixture fixture) => _fixture = fixture;

    // TestMongoFixture exposes NewDatabase() (sync, fresh db per call). The
    // plan's FreshDatabaseAsync() helper does not exist on this fixture; a
    // fresh db needs no async setup for the match-reports collection.
    private IMongoDatabase FreshDb() => _fixture.NewDatabase();

    [Fact]
    public async Task Insert_then_GetById_roundtrips()
    {
        var db = FreshDb();
        var repo = new MatchReportRepository(db);
        var id = Guid.NewGuid();
        var report = new MatchReport
        {
            Id = id, ReporterSub = "alice", MatchId = Guid.NewGuid(),
            Title = "Boltwave wedge", Status = ReportStatus.IssueOpen,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };

        await repo.InsertAsync(report, default);
        var loaded = await repo.GetByIdAsync(id, default);

        loaded.Should().NotBeNull();
        loaded!.ReporterSub.Should().Be("alice");
        loaded.Status.Should().Be(ReportStatus.IssueOpen);
    }

    [Fact]
    public async Task CountBySubSince_counts_only_recent_for_that_sub()
    {
        var db = FreshDb();
        var repo = new MatchReportRepository(db);
        async Task Seed(string sub, DateTime at) => await repo.InsertAsync(new MatchReport
        {
            Id = Guid.NewGuid(), ReporterSub = sub, MatchId = Guid.NewGuid(),
            Title = "t", Status = ReportStatus.IssueOpen, CreatedAt = at, UpdatedAt = at,
        }, default);

        var now = DateTime.UtcNow;
        await Seed("alice", now);
        await Seed("alice", now.AddHours(-2));
        await Seed("bob", now);

        var count = await repo.CountBySubSinceAsync("alice", now.AddHours(-1), default);
        count.Should().Be(1);
    }
}
