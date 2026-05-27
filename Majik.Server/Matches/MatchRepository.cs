using MongoDB.Driver;

namespace Majik.Server.Matches;

/// <summary>Mongo-backed access to <see cref="Match"/> documents. Concrete
/// class — no interface yet (matches the <c>UserProfileRepository</c>
/// pattern). All state transitions go through <see cref="TryAtomicUpdateAsync"/>
/// to make races safe.
///
/// <para>Not sealed and its data methods are <c>virtual</c> so reliability
/// tests can subclass it to inject transient faults (the retry-policy
/// coverage in <c>MatchServiceRetryTests</c>); the default production graph
/// uses the un-overridden Mongo implementation.</para></summary>
public class MatchRepository
{
    private const string CollectionName = "matches";
    private readonly IMongoCollection<Match> _collection;

    public MatchRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<Match>(CollectionName);
    }

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var unique = new CreateIndexOptions { Unique = true };
        var idIndex = new CreateIndexModel<Match>(
            Builders<Match>.IndexKeys.Ascending(x => x.Id), unique);
        var lobbyIndex = new CreateIndexModel<Match>(
            Builders<Match>.IndexKeys.Ascending(x => x.State).Ascending(x => x.Visibility));
        var creatorIndex = new CreateIndexModel<Match>(
            Builders<Match>.IndexKeys.Ascending("creator.sub"));
        var opponentIndex = new CreateIndexModel<Match>(
            Builders<Match>.IndexKeys.Ascending("opponent.sub"));
        await _collection.Indexes.CreateManyAsync(
            new[] { idIndex, lobbyIndex, creatorIndex, opponentIndex },
            ct);
    }

    public virtual Task InsertAsync(Match m, CancellationToken ct) =>
        _collection.InsertOneAsync(m, cancellationToken: ct);

    public virtual Task<Match?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;

    public virtual async Task<IReadOnlyList<Match>> ListOpenPublicAsync(int limit, CancellationToken ct)
    {
        // Bot matches synthesize an Opponent at create time and are stamped
        // Invite, but defense-in-depth: also exclude any doc whose opponent
        // sub starts with "bot:" so a future code-path that mis-stamps a
        // public bot match can't leak into the lobby.
        var found = await _collection
            .Find(x => x.State == MatchState.Open
                       && x.Visibility == MatchVisibility.Public
                       && (x.Opponent == null || !x.Opponent.Sub.StartsWith("bot:")))
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);
        return found;
    }

    public virtual async Task<IReadOnlyList<Match>> ListInStateAsync(MatchState state, CancellationToken ct)
    {
        var found = await _collection
            .Find(x => x.State == state)
            .ToListAsync(ct);
        return found;
    }

    public virtual async Task<bool> TryAtomicUpdateAsync(
        Guid id,
        MatchState expectedState,
        UpdateDefinition<Match> update,
        CancellationToken ct)
    {
        var result = await _collection.UpdateOneAsync(
            Builders<Match>.Filter.Where(x => x.Id == id && x.State == expectedState),
            update,
            cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    /// <summary>
    /// Compare-and-swap variant that additionally filters on the expected
    /// current <see cref="Match.PriorityHolderSub"/> AND, when supplied, the
    /// expected <see cref="Match.PriorityStartedAt"/>. Used by the clock
    /// handoff (<c>MatchService.OnPriorityPassedAsync</c>) so two rapid,
    /// out-of-order handoffs (A→B then B→A on fast turn cycling /
    /// bot-vs-bot) can't both read the same prior holder and double-bill or
    /// drop a transition: only the update whose <paramref name="expectedPriorityHolderSub"/>
    /// still matches the stored value wins; the loser matches nothing and
    /// returns false. A null holder expectation matches a stored null holder.
    ///
    /// <para>When <paramref name="constrainStartedAt"/> is true the CAS is
    /// tightened to also require <paramref name="expectedPriorityStartedAt"/>,
    /// so a duplicate / late handoff that observed the SAME (holder, startedAt)
    /// pair as the winner can't apply a second deduction off the same slice —
    /// the winner advances PriorityStartedAt, so the duplicate's filter misses
    /// and it no-ops (Slice 4a #6). When false (the default, for direct callers
    /// / legacy tests) the timestamp is not part of the filter.</para>
    /// </summary>
    public virtual async Task<bool> TryAtomicUpdateWithHolderAsync(
        Guid id,
        MatchState expectedState,
        string? expectedPriorityHolderSub,
        UpdateDefinition<Match> update,
        CancellationToken ct,
        bool constrainStartedAt = false,
        DateTime? expectedPriorityStartedAt = null)
    {
        var filter = Builders<Match>.Filter.Where(x =>
            x.Id == id
            && x.State == expectedState
            && x.PriorityHolderSub == expectedPriorityHolderSub);

        if (constrainStartedAt)
        {
            filter = Builders<Match>.Filter.And(
                filter,
                Builders<Match>.Filter.Eq(x => x.PriorityStartedAt, expectedPriorityStartedAt));
        }

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public virtual Task<long> DeleteByIdAsync(Guid id, CancellationToken ct)
    {
        return _collection.DeleteOneAsync(x => x.Id == id, ct)
            .ContinueWith(t => t.Result.DeletedCount, ct);
    }
}
