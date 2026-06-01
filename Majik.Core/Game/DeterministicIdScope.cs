using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using Majik.Core.Players.Agents;
using Majik.Core.Random;

namespace Majik.Core.Game;

/// <summary>
/// PLAN 08 — a per-game <b>deterministic object-id source</b>. Completes the
/// determinism story the <see cref="LogicalClock"/> / pinned-seed work started:
/// the portal-facing object ids (<c>Card.InstanceId</c> → <c>cardId</c>,
/// <c>Spell.Id</c> → <c>stackId</c>, ability ids, <c>Player.Id</c> →
/// <c>controllerId</c>, <c>Target.Id</c>, <c>Emblem.Id</c>) were minted with
/// <see cref="System.Guid.NewGuid"/>, so a same-(seed, command-order) replay
/// produced DIFFERENT ids. Because the portal keys its reducer by these ids,
/// id-divergence blocked client-facing rehydration: <c>GameFacade.FromSnapshot</c>
/// could only reach STRUCTURAL equivalence, never id-identity.
///
/// <para>
/// This source replaces those <see cref="System.Guid.NewGuid"/> reads with a
/// strictly-increasing per-game counter folded together with the game seed into
/// a deterministic <see cref="System.Guid"/>. Because the counter is bumped at
/// the exact same construction points <see cref="System.Guid.NewGuid"/> was
/// read, the Nth object minted in a game (given the same seed + same command
/// order) ALWAYS gets the same id on replay. The id keeps the <see cref="Guid"/>
/// type at every call site, so the portal/JSON contract is unchanged — only the
/// SOURCE of the value moves from random to seed-derived.
/// </para>
///
/// <para>
/// <b>Cross-game uniqueness is intentionally given up.</b> Two concurrent games
/// started with the same seed mint the SAME id sequence. That is safe here
/// because every consumer of these ids is per-game scoped: the per-game
/// registries (<see cref="AgentRegistry"/>, <see cref="GameRandomRegistry"/>,
/// …) are ambient-scoped via <see cref="AmbientRegistryStore{TStore}"/> so a
/// <c>Player.Id</c> collision across games can't cross registries; the server's
/// Mongo <c>_id</c>/Redis keys are keyed by the (globally-unique, server-minted)
/// MATCH id, never by these in-game ids. See the global-uniqueness audit in the
/// PR that introduced this type.
/// </para>
/// </summary>
public interface IDeterministicIdSource
{
    /// <summary>
    /// Next deterministic <see cref="System.Guid"/> for this game. Strictly
    /// reproducible: the Nth call for a given seed always returns the same id.
    /// </summary>
    Guid NextId();
}

/// <summary>
/// Default <see cref="IDeterministicIdSource"/>: a thread-safe monotonic counter
/// folded with the game seed via SHA-256 into a stable <see cref="System.Guid"/>.
/// </summary>
public sealed class DeterministicIdSource : IDeterministicIdSource
{
    private readonly int _seed;
    private long _counter;

    /// <param name="seed">The game seed (the same <see cref="GameRandom.Seed"/>
    /// the run was started with). Folding the seed in means two games with
    /// different seeds get different id sequences, while replay (same seed)
    /// reproduces the sequence exactly.</param>
    public DeterministicIdSource(int seed) => _seed = seed;

    public Guid NextId()
    {
        var n = Interlocked.Increment(ref _counter);
        return Compose(_seed, n);
    }

    /// <summary>
    /// Deterministically compose (seed, counter) into a <see cref="System.Guid"/>.
    /// SHA-256 over the 12 little-endian bytes of (seed, counter) gives a
    /// well-distributed digest; the first 16 bytes become the Guid. Stable
    /// across processes/runtimes/architectures (no endianness or hash-seed
    /// dependence), so a snapshot replayed on a different host reproduces the
    /// same ids.
    /// </summary>
    internal static Guid Compose(int seed, long counter)
    {
        Span<byte> input = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(input[..4], seed);
        BinaryPrimitives.WriteInt64LittleEndian(input[4..], counter);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);

        return new Guid(digest[..16]);
    }
}

/// <summary>
/// Ambient accessor for the active per-game <see cref="IDeterministicIdSource"/>.
///
/// <para>
/// Installed for the duration of a game's run via <see cref="Push"/> (an
/// <see cref="System.Threading.AsyncLocal{T}"/> scope that flows across the
/// engine's <c>await</c> continuations), exactly mirroring
/// <see cref="LogicalClockScope"/>. Every object constructed while the game
/// advances — on whatever threadpool thread a continuation resumes on — mints
/// its id from THIS game's source. Concurrent games run on independent async
/// flows and therefore mint from independent sources (so a parallel game never
/// steals this game's counter).
/// </para>
///
/// <para>
/// When NO per-game source is installed (the bulk of the unit-test suite, which
/// constructs cards / abilities / players directly with no surrounding game),
/// <see cref="NewId"/> falls back to <see cref="System.Guid.NewGuid"/> — the
/// pre-existing behaviour. So direct-construction tests keep getting globally
/// unique random ids and are unaffected; only objects minted INSIDE a pushed
/// game scope become deterministic.
/// </para>
/// </summary>
public static class DeterministicIdScope
{
    private static readonly AsyncLocal<IDeterministicIdSource?> _ambient = new();

    /// <summary>
    /// The active deterministic id source for the current async flow, or
    /// <c>null</c> when none is installed (direct-construction tests).
    /// </summary>
    public static IDeterministicIdSource? Current => _ambient.Value;

    /// <summary>
    /// The id every reseeded call site uses in place of
    /// <see cref="System.Guid.NewGuid"/>: the per-game deterministic id when a
    /// scope is installed, otherwise a fresh random <see cref="System.Guid"/>
    /// (preserving the prior behaviour for scope-less construction).
    /// </summary>
    public static Guid NewId() => _ambient.Value?.NextId() ?? Guid.NewGuid();

    /// <summary>
    /// Install <paramref name="source"/> as the ambient id source for the
    /// current async flow until the returned scope is disposed. Nesting restores
    /// the previous source on dispose.
    /// </summary>
    public static IDisposable Push(IDeterministicIdSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var previous = _ambient.Value;
        _ambient.Value = source;
        return new Scope(previous);
    }

    /// <summary>
    /// Install <paramref name="source"/> as the ambient id source ONLY if no
    /// source is currently active; otherwise return a no-op handle that leaves
    /// the existing source in place. Used by the game driver so that when a
    /// replay/determinism harness has already installed ONE source spanning both
    /// the initial-board construction and the run, the driver continues that
    /// source's monotonic counter instead of restarting it (which would collide
    /// run-minted ids with the board ids). When nothing is active the driver
    /// installs its own seed-derived source as before.
    /// </summary>
    public static IDisposable PushIfNone(IDeterministicIdSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _ambient.Value is null ? Push(source) : NoopScope.Instance;
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }

    private sealed class Scope : IDisposable
    {
        private readonly IDeterministicIdSource? _previous;
        private bool _disposed;

        public Scope(IDeterministicIdSource? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ambient.Value = _previous;
        }
    }
}
