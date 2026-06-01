using System.Text.Json;
using Majik.Core.Api;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Majik.Server.Matches.Persistence;

/// <summary>
/// Mongo-backed durable <see cref="IEngineCheckpointStore"/>. One document per
/// match in the <c>engineCheckpoint</c> collection (keyed by matchId via a
/// unique index), upserted so the latest checkpoint replaces the prior one.
/// The <see cref="GameSnapshot"/> (state + bundled command prefix + seed) is
/// stored as a JSON string for the same polymorphism-friendly reasons as the
/// command log.
/// </summary>
public class MongoEngineCheckpointStore : IEngineCheckpointStore
{
    private const string CollectionName = "engineCheckpoint";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IMongoCollection<CheckpointDoc> _collection;

    public MongoEngineCheckpointStore(IMongoDatabase database)
    {
        _collection = database.GetCollection<CheckpointDoc>(CollectionName);
    }

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var unique = new CreateIndexOptions { Unique = true };
        var idx = new CreateIndexModel<CheckpointDoc>(
            Builders<CheckpointDoc>.IndexKeys.Ascending(x => x.MatchId), unique);
        await _collection.Indexes.CreateOneAsync(idx, cancellationToken: ct);
    }

    public virtual async Task SaveAsync(EngineCheckpoint checkpoint, CancellationToken ct)
    {
        var doc = new CheckpointDoc
        {
            MatchId = checkpoint.MatchId,
            LastAppliedSeq = checkpoint.LastAppliedSeq,
            Seed = checkpoint.Seed,
            At = checkpoint.At,
            SnapshotJson = JsonSerializer.Serialize(checkpoint.Snapshot, JsonOptions),
        };

        // Upsert by matchId, but only advance to a checkpoint that reflects at
        // least as many applied commands (guards an out-of-order late write from
        // regressing the checkpoint).
        var filter = Builders<CheckpointDoc>.Filter.And(
            Builders<CheckpointDoc>.Filter.Eq(x => x.MatchId, checkpoint.MatchId),
            Builders<CheckpointDoc>.Filter.Lte(x => x.LastAppliedSeq, checkpoint.LastAppliedSeq));

        var update = Builders<CheckpointDoc>.Update
            .Set(x => x.MatchId, doc.MatchId)
            .Set(x => x.LastAppliedSeq, doc.LastAppliedSeq)
            .Set(x => x.Seed, doc.Seed)
            .Set(x => x.At, doc.At)
            .Set(x => x.SnapshotJson, doc.SnapshotJson);

        try
        {
            await _collection.UpdateOneAsync(
                filter, update, new UpdateOptions { IsUpsert = true }, ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A concurrent writer already advanced the checkpoint past this seq
            // (the filter's Lte guard rejected our upsert as a fresh insert).
            // Benign — the more-advanced checkpoint stands.
        }
    }

    public virtual async Task<EngineCheckpoint?> GetLatestAsync(Guid matchId, CancellationToken ct)
    {
        var doc = await _collection
            .Find(x => x.MatchId == matchId)
            .FirstOrDefaultAsync(ct);
        if (doc == null) return null;

        var snapshot = JsonSerializer.Deserialize<GameSnapshot>(doc.SnapshotJson, JsonOptions)!;
        return new EngineCheckpoint(doc.MatchId, doc.LastAppliedSeq, doc.Seed, snapshot, doc.At);
    }

    internal sealed class CheckpointDoc
    {
        [BsonId] public ObjectId InternalId { get; set; }

        [BsonElement("matchId")]
        [BsonRepresentation(BsonType.String)]
        public Guid MatchId { get; set; }

        [BsonElement("lastAppliedSeq")] public long LastAppliedSeq { get; set; }
        [BsonElement("seed")] public int Seed { get; set; }
        [BsonElement("at")] public DateTime At { get; set; }
        [BsonElement("snapshotJson")] public required string SnapshotJson { get; set; }
    }
}
