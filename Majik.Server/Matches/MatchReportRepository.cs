using MongoDB.Driver;

namespace Majik.Server.Matches;

/// <summary>Thin Mongo wrapper for <see cref="MatchReport"/> status records.
/// Not sealed and its data methods are <c>virtual</c> so the service tests can
/// mock it (mirrors <see cref="MatchRepository"/>).</summary>
public class MatchReportRepository
{
    private const string CollectionName = "match-reports";
    private readonly IMongoCollection<MatchReport> _collection;

    public MatchReportRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<MatchReport>(CollectionName);
    }

    public virtual Task InsertAsync(MatchReport report, CancellationToken ct) =>
        _collection.InsertOneAsync(report, cancellationToken: ct);

    public virtual Task<MatchReport?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;

    /// <summary>Count reports a sub has filed since <paramref name="since"/>
    /// (rate-limit input).</summary>
    public virtual Task<long> CountBySubSinceAsync(string sub, DateTime since, CancellationToken ct) =>
        _collection.CountDocumentsAsync(x => x.ReporterSub == sub && x.CreatedAt >= since, cancellationToken: ct);

    /// <summary>Look up a report by the GitHub issue number a fix PR closes
    /// (webhook entry point). Returns null if no report tracks that issue.</summary>
    public virtual Task<MatchReport?> GetByIssueNumberAsync(int issueNumber, CancellationToken ct) =>
        _collection.Find(x => x.IssueNumber == issueNumber).FirstOrDefaultAsync(ct)!;

    /// <summary>Advance a report to <see cref="ReportStatus.Merged"/>, recording
    /// the fix repo + merge time the DeploymentWatcher uses to detect redeploy.</summary>
    public virtual async Task MarkMergedAsync(Guid id, string repo, DateTime mergedAt, CancellationToken ct)
    {
        var update = Builders<MatchReport>.Update
            .Set(x => x.Status, ReportStatus.Merged)
            .Set(x => x.Repo, repo)
            .Set(x => x.FixMergedAt, mergedAt)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _collection.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
    }

    /// <summary>Advance a report to <see cref="ReportStatus.Delivered"/> once the
    /// fix is live (DeploymentWatcher terminal transition).</summary>
    public virtual async Task MarkDeliveredAsync(Guid id, CancellationToken ct)
    {
        var update = Builders<MatchReport>.Update
            .Set(x => x.Status, ReportStatus.Delivered)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _collection.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
    }

    /// <summary>All reports currently in <paramref name="status"/> (the watcher
    /// sweeps the Merged set each tick).</summary>
    public virtual Task<List<MatchReport>> ListByStatusAsync(ReportStatus status, CancellationToken ct) =>
        _collection.Find(x => x.Status == status).ToListAsync(ct);
}
