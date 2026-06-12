using System.Text.Json;
using Majik.Core.Api.BotReplay;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// Mongo-backed durable <see cref="IBotDecisionLogStore"/>. One document per
/// (matchId, botSeq) in the <c>engineBotDecisionLog</c> collection, with a
/// UNIQUE index on (matchId, botSeq) for idempotency: a duplicate append is
/// swallowed as a benign duplicate-key.
///
/// <para>The polymorphic <see cref="BotDecisionRecord"/> is stored as a JSON
/// string (System.Text.Json with the payload's <c>$type</c> discriminator)
/// rather than a BSON object graph — the same wire encoding
/// <see cref="MongoEngineCommandLogStore"/> uses for <c>GameCommand</c>, so
/// every payload shape round-trips without bespoke BSON class maps.</para>
/// </summary>
public class MongoBotDecisionLogStore : IBotDecisionLogStore
{
    private const string CollectionName = "engineBotDecisionLog";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IMongoCollection<LogEntry> _collection;

    public MongoBotDecisionLogStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<LogEntry>(CollectionName);
    }

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var unique = new CreateIndexOptions { Unique = true };
        var idx = new CreateIndexModel<LogEntry>(
            Builders<LogEntry>.IndexKeys.Ascending(x => x.MatchId).Ascending(x => x.BotSeq),
            unique);
        await _collection.Indexes.CreateOneAsync(idx, cancellationToken: ct);
    }

    public virtual async Task AppendAsync(Guid matchId, BotDecisionRecord record, CancellationToken ct)
    {
        var entry = new LogEntry
        {
            MatchId = matchId,
            BotSeq = record.BotSeq,
            RecordJson = JsonSerializer.Serialize(record, JsonOptions),
        };
        try
        {
            await _collection.InsertOneAsync(entry, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Idempotent on (matchId, botSeq) — the entry already exists; the
            // first write stands. Expected, benign path for a retried append.
        }
    }

    public virtual async Task<IReadOnlyList<BotDecisionRecord>> ReadAllAsync(
        Guid matchId, CancellationToken ct)
    {
        var entries = await _collection
            .Find(x => x.MatchId == matchId)
            .SortBy(x => x.BotSeq)
            .ToListAsync(ct);

        return entries
            .Select(e => JsonSerializer.Deserialize<BotDecisionRecord>(e.RecordJson, JsonOptions)!)
            .ToList();
    }

    internal sealed class LogEntry
    {
        [BsonId] public ObjectId InternalId { get; set; }

        [BsonElement("matchId")]
        [BsonRepresentation(BsonType.String)]
        public Guid MatchId { get; set; }

        [BsonElement("botSeq")] public int BotSeq { get; set; }
        [BsonElement("recordJson")] public required string RecordJson { get; set; }
    }
}
