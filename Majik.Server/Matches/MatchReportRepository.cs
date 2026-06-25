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
}
