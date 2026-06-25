using FluentAssertions;
using Majik.Server.Matches;
using Majik.Server.Tests.Profiles;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class MatchReportRepositoryStatusTests : IClassFixture<TestMongoFixture>
{
    private readonly TestMongoFixture _fixture;
    public MatchReportRepositoryStatusTests(TestMongoFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetByIssueNumber_then_mark_merged_then_list_merged()
    {
        // TestMongoFixture exposes NewDatabase() (sync, fresh db per call); the
        // plan's FreshDatabaseAsync() helper does not exist on this fixture.
        var db = _fixture.NewDatabase();
        var repo = new MatchReportRepository(db);
        var now = DateTime.UtcNow;
        await repo.InsertAsync(new MatchReport
        {
            Id = Guid.NewGuid(), ReporterSub = "alice", MatchId = Guid.NewGuid(),
            Title = "wedge", Status = ReportStatus.IssueOpen, IssueNumber = 50,
            CreatedAt = now, UpdatedAt = now,
        }, default);

        var found = await repo.GetByIssueNumberAsync(50, default);
        found.Should().NotBeNull();

        await repo.MarkMergedAsync(found!.Id, "bg9m9r/majik", now, default);

        var merged = await repo.ListByStatusAsync(ReportStatus.Merged, default);
        merged.Should().ContainSingle(r => r.IssueNumber == 50 && r.Repo == "bg9m9r/majik"
            && r.FixMergedAt != null);
    }

    [Fact]
    public async Task MarkDelivered_flips_status_to_delivered()
    {
        var db = _fixture.NewDatabase();
        var repo = new MatchReportRepository(db);
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        await repo.InsertAsync(new MatchReport
        {
            Id = id, ReporterSub = "bob", MatchId = Guid.NewGuid(),
            Title = "wedge2", Status = ReportStatus.Merged, IssueNumber = 51,
            Repo = "bg9m9r/majik", FixMergedAt = now, CreatedAt = now, UpdatedAt = now,
        }, default);

        await repo.MarkDeliveredAsync(id, default);

        var delivered = await repo.ListByStatusAsync(ReportStatus.Delivered, default);
        delivered.Should().ContainSingle(r => r.Id == id);
        var stillMerged = await repo.ListByStatusAsync(ReportStatus.Merged, default);
        stillMerged.Should().BeEmpty();
    }
}
