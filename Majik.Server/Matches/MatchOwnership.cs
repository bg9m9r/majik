using System.Collections.Concurrent;
using Majik.Server.Composition;
using StackExchange.Redis;

namespace Majik.Server.Matches;

/// <summary>
/// Tracks which majik-api replica owns the in-memory <c>GameFacade</c> for
/// a given match. Backed by Redis keys of the form
/// <c>match:{matchId}:owner</c> with a TTL — the owning instance refreshes
/// the TTL on a heartbeat so a crashed owner naturally releases the lock.
///
/// PR 5 of the scaling chain: claim + heartbeat + release. Cross-replica
/// command forwarding (PR 6) reads <see cref="GetOwnerAsync"/> to decide
/// whether to dispatch locally or publish over Redis pub/sub.
///
/// Behavior with no Redis configured: TryClaim always returns true (single
/// process owns everything); GetOwnerAsync returns the local instance id;
/// Release is a no-op. This keeps the single-replica deploy unchanged.
/// </summary>
public interface IMatchOwnership
{
    /// <summary>Try to claim ownership of <paramref name="matchId"/>.
    /// Returns true if claimed or already owned by this instance; false if
    /// another instance currently owns it.</summary>
    Task<bool> TryClaimAsync(Guid matchId, CancellationToken ct);

    /// <summary>Refresh the TTL on a match this instance owns. Returns true
    /// if still owned (TTL refreshed); false if ownership was lost
    /// (e.g. TTL expired and someone else claimed it).</summary>
    Task<bool> RefreshAsync(Guid matchId, CancellationToken ct);

    /// <summary>Read the current owner. Returns the instance id, or null
    /// when no owner is recorded.</summary>
    Task<string?> GetOwnerAsync(Guid matchId, CancellationToken ct);

    /// <summary>Release ownership (typically on game-over). Idempotent.
    /// Only releases when this instance is the recorded owner — never
    /// clobbers another instance's claim.</summary>
    Task ReleaseAsync(Guid matchId, CancellationToken ct);

    /// <summary>Matches this instance currently believes it owns. Surface
    /// for the heartbeat hosted service.</summary>
    IReadOnlyCollection<Guid> Owned { get; }
}

public sealed class MatchOwnership : IMatchOwnership
{
    /// <summary>TTL on the owner key. Heartbeat refreshes well below this.</summary>
    public static readonly TimeSpan OwnerTtl = TimeSpan.FromSeconds(30);

    private readonly IConnectionMultiplexer? _redis;
    private readonly IInstanceIdProvider _instanceIds;
    private readonly ConcurrentDictionary<Guid, byte> _owned = new();

    public MatchOwnership(IInstanceIdProvider instanceIds, IConnectionMultiplexer? redis = null)
    {
        _instanceIds = instanceIds;
        _redis = redis;
    }

    public IReadOnlyCollection<Guid> Owned => _owned.Keys.ToList();

    private static string Key(Guid matchId) => $"match:{matchId:N}:owner";

    public async Task<bool> TryClaimAsync(Guid matchId, CancellationToken ct)
    {
        if (_redis == null)
        {
            // Single-process mode — there's no contention. Record locally so
            // GetOwnerAsync continues to return this instance.
            _owned.TryAdd(matchId, 0);
            return true;
        }

        var db = _redis.GetDatabase();
        var key = Key(matchId);
        var me = _instanceIds.Value;

        // SETNX (NX). If success → fresh claim. If a key exists for this
        // instance already, we still consider it owned (avoids losing
        // ownership across a redeploy that lands on the same instance).
        var claimed = await db.StringSetAsync(key, me, OwnerTtl, When.NotExists);
        if (claimed)
        {
            _owned.TryAdd(matchId, 0);
            return true;
        }

        var current = (string?)await db.StringGetAsync(key);
        if (current == me)
        {
            _owned.TryAdd(matchId, 0);
            // Refresh the TTL so a stale entry doesn't expire mid-game.
            await db.KeyExpireAsync(key, OwnerTtl);
            return true;
        }
        return false;
    }

    public async Task<bool> RefreshAsync(Guid matchId, CancellationToken ct)
    {
        if (_redis == null)
        {
            return _owned.ContainsKey(matchId);
        }

        var db = _redis.GetDatabase();
        var key = Key(matchId);
        var current = (string?)await db.StringGetAsync(key);
        if (current != _instanceIds.Value)
        {
            _owned.TryRemove(matchId, out _);
            return false;
        }
        await db.KeyExpireAsync(key, OwnerTtl);
        return true;
    }

    public async Task<string?> GetOwnerAsync(Guid matchId, CancellationToken ct)
    {
        if (_redis == null)
        {
            return _owned.ContainsKey(matchId) ? _instanceIds.Value : null;
        }

        var db = _redis.GetDatabase();
        var current = (string?)await db.StringGetAsync(Key(matchId));
        return current;
    }

    public async Task ReleaseAsync(Guid matchId, CancellationToken ct)
    {
        _owned.TryRemove(matchId, out _);
        if (_redis == null) return;

        var db = _redis.GetDatabase();
        var key = Key(matchId);
        // CAS-style release — only delete if we still own it. Avoids clobbering
        // a new owner that already took over after our TTL expired.
        var current = (string?)await db.StringGetAsync(key);
        if (current == _instanceIds.Value)
        {
            await db.KeyDeleteAsync(key);
        }
    }
}
