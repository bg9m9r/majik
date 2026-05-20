using MongoDB.Driver;

namespace Majik.Server.Decks;

/// <summary>Mongo-backed access to <see cref="Deck"/> documents.
/// Sealed concrete class — matches the UserProfileRepository / MatchRepository
/// pattern from sub-projects #1 and #5.</summary>
public sealed class DeckRepository
{
    private const string CollectionName = "decks";
    private readonly IMongoCollection<Deck> _collection;

    public DeckRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<Deck>(CollectionName);
    }

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var unique = new CreateIndexOptions { Unique = true };
        var idIndex = new CreateIndexModel<Deck>(
            Builders<Deck>.IndexKeys.Ascending(d => d.Id), unique);
        var ownerNameIndex = new CreateIndexModel<Deck>(
            Builders<Deck>.IndexKeys.Ascending(d => d.OwnerSub).Ascending(d => d.Name),
            unique);
        var ownerIndex = new CreateIndexModel<Deck>(
            Builders<Deck>.IndexKeys.Ascending(d => d.OwnerSub));
        await _collection.Indexes.CreateManyAsync(
            new[] { idIndex, ownerNameIndex, ownerIndex }, ct);
    }

    public Task InsertAsync(Deck d, CancellationToken ct) =>
        _collection.InsertOneAsync(d, cancellationToken: ct);

    public Task<Deck?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct)!;

    public Task<Deck?> GetByIdForOwnerAsync(Guid id, string ownerSub, CancellationToken ct) =>
        _collection
            .Find(d => d.Id == id && d.OwnerSub == ownerSub)
            .FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyList<Deck>> ListByOwnerAsync(string ownerSub, CancellationToken ct)
    {
        var found = await _collection
            .Find(d => d.OwnerSub == ownerSub)
            .SortByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);
        return found;
    }

    public Task<long> CountByOwnerAsync(string ownerSub, CancellationToken ct) =>
        _collection.CountDocumentsAsync(d => d.OwnerSub == ownerSub, cancellationToken: ct);

    public async Task<bool> NameTakenForOwnerAsync(string ownerSub, string name, Guid? excludeId, CancellationToken ct)
    {
        var filter = Builders<Deck>.Filter.Eq(d => d.OwnerSub, ownerSub)
                   & Builders<Deck>.Filter.Eq(d => d.Name, name);
        if (excludeId.HasValue)
        {
            filter &= Builders<Deck>.Filter.Ne(d => d.Id, excludeId.Value);
        }
        var count = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        return count > 0;
    }

    public async Task<bool> UpdateForOwnerAsync(
        Guid id,
        string ownerSub,
        string newName,
        IReadOnlyList<DeckCardEntry> newMainboard,
        IReadOnlyList<DeckCardEntry> newSideboard,
        DateTime now,
        CancellationToken ct)
    {
        var update = Builders<Deck>.Update
            .Set(d => d.Name, newName)
            .Set(d => d.Mainboard, newMainboard.ToList())
            .Set(d => d.Sideboard, newSideboard.ToList())
            .Set(d => d.UpdatedAt, now);

        var result = await _collection.UpdateOneAsync(
            Builders<Deck>.Filter.Where(d => d.Id == id && d.OwnerSub == ownerSub),
            update,
            cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<long> DeleteForOwnerAsync(Guid id, string ownerSub, CancellationToken ct)
    {
        var result = await _collection.DeleteOneAsync(
            d => d.Id == id && d.OwnerSub == ownerSub,
            ct);
        return result.DeletedCount;
    }
}
