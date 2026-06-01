using System.Text.Json;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// Mongo-backed durable <see cref="IEngineCommandLogStore"/>. One document per
/// (matchId, seq) in the <c>engineCommandLog</c> collection, with a UNIQUE index
/// on (matchId, seq) for idempotency: a duplicate append is swallowed as a
/// benign duplicate-key, so a forwarded-then-retried command or a double-dispatch
/// across a claim handoff can't corrupt the stream.
///
/// <para>The polymorphic <see cref="GameCommand"/> is stored as a JSON string
/// (System.Text.Json with the command's <c>$type</c> discriminator) rather than
/// a BSON object graph — same wire encoding the cross-replica forwarder uses, so
/// every command shape round-trips without bespoke BSON class maps.</para>
/// </summary>
public class MongoEngineCommandLogStore : IEngineCommandLogStore
{
    private const string CollectionName = "engineCommandLog";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IMongoCollection<LogEntry> _collection;

    public MongoEngineCommandLogStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<LogEntry>(CollectionName);
    }

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var unique = new CreateIndexOptions { Unique = true };
        var idx = new CreateIndexModel<LogEntry>(
            Builders<LogEntry>.IndexKeys.Ascending(x => x.MatchId).Ascending(x => x.Seq),
            unique);
        await _collection.Indexes.CreateOneAsync(idx, cancellationToken: ct);
    }

    public virtual async Task AppendAsync(
        Guid matchId, long seq, DateTime at, GameCommand command, CancellationToken ct)
    {
        var entry = new LogEntry
        {
            MatchId = matchId,
            Seq = seq,
            At = at,
            CommandJson = JsonSerializer.Serialize<GameCommand>(command, JsonOptions),
        };
        try
        {
            await _collection.InsertOneAsync(entry, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Idempotent on (matchId, seq) — the entry already exists; the first
            // write stands. This is the expected, benign path for a replayed /
            // re-forwarded command, NOT an error.
        }
    }

    public virtual async Task<IReadOnlyList<LoggedCommand>> ReadSinceAsync(
        Guid matchId, long afterSeq, CancellationToken ct)
    {
        var entries = await _collection
            .Find(x => x.MatchId == matchId && x.Seq > afterSeq)
            .SortBy(x => x.Seq)
            .ToListAsync(ct);

        return entries
            .Select(e => new LoggedCommand(
                e.At,
                JsonSerializer.Deserialize<GameCommand>(e.CommandJson, JsonOptions)!))
            .ToList();
    }

    public virtual async Task<long> MaxSeqAsync(Guid matchId, CancellationToken ct)
    {
        var top = await _collection
            .Find(x => x.MatchId == matchId)
            .SortByDescending(x => x.Seq)
            .Limit(1)
            .FirstOrDefaultAsync(ct);
        return top?.Seq ?? -1L;
    }

    internal sealed class LogEntry
    {
        [BsonId] public ObjectId InternalId { get; set; }

        [BsonElement("matchId")]
        [BsonRepresentation(BsonType.String)]
        public Guid MatchId { get; set; }

        [BsonElement("seq")] public long Seq { get; set; }
        [BsonElement("at")] public DateTime At { get; set; }
        [BsonElement("commandJson")] public required string CommandJson { get; set; }
    }
}
