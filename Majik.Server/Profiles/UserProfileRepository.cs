using MongoDB.Driver;

namespace Majik.Server.Profiles;

/// <summary>Mongo-backed access to <see cref="UserProfile"/> documents.
/// Concrete class — no interface yet, per spec. Wrap once we need test
/// doubles in code beyond the repository tests themselves.</summary>
public sealed class UserProfileRepository
{
    private const string CollectionName = "userProfiles";
    private readonly IMongoCollection<UserProfile> _collection;

    public UserProfileRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<UserProfile>(CollectionName);
    }

    /// <summary>Idempotent. Safe to call on every startup.</summary>
    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var unique = new CreateIndexOptions { Unique = true };
        var subIndex = new CreateIndexModel<UserProfile>(
            Builders<UserProfile>.IndexKeys.Ascending(x => x.Sub), unique);
        var handleIndex = new CreateIndexModel<UserProfile>(
            Builders<UserProfile>.IndexKeys.Ascending(x => x.Handle), unique);
        await _collection.Indexes.CreateManyAsync(new[] { subIndex, handleIndex }, ct);
    }

    public Task<UserProfile?> GetBySubAsync(string sub, CancellationToken ct) =>
        _collection
            .Find(p => p.Sub == sub)
            .FirstOrDefaultAsync(ct)!;

    /// <summary>Insert-or-update by <see cref="UserProfile.Sub"/>.
    /// Uses <c>UpdateOneAsync</c> with <c>IsUpsert = true</c> so that a
    /// duplicate-key violation on the <em>handle</em> index surfaces as
    /// <see cref="MongoWriteException"/> (not <see cref="MongoCommandException"/>
    /// which <c>FindOneAndUpdateAsync</c> would throw in driver v3).</summary>
    public async Task<UserProfile> UpsertAsync(UserProfile profile, CancellationToken ct)
    {
        var filter = Builders<UserProfile>.Filter.Eq(p => p.Sub, profile.Sub);
        var update = Builders<UserProfile>.Update
            .SetOnInsert(p => p.Sub, profile.Sub)
            .SetOnInsert(p => p.CreatedAt, profile.CreatedAt)
            .Set(p => p.Handle, profile.Handle)
            .Set(p => p.HandleDisplay, profile.HandleDisplay)
            .Set(p => p.UpdatedAt, profile.UpdatedAt);
        var options = new UpdateOptions { IsUpsert = true };

        // Throws MongoWriteException on duplicate-key violation (e.g. handle clash).
        await _collection.UpdateOneAsync(filter, update, options, ct);

        return (await GetBySubAsync(profile.Sub, ct))!;
    }

    public async Task<bool> IsHandleTakenAsync(string handleLower, string? excludeSub, CancellationToken ct)
    {
        var filter = Builders<UserProfile>.Filter.Eq(p => p.Handle, handleLower);
        if (excludeSub != null)
        {
            filter &= Builders<UserProfile>.Filter.Ne(p => p.Sub, excludeSub);
        }
        var count = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        return count > 0;
    }
}
