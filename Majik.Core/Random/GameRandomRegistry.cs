using Majik.Core.Game;

namespace Majik.Core.Random;

/// <summary>
/// Thread-safe per-game <see cref="GameRandom"/> registry. Effect
/// closures (e.g. tutor factories invoking <c>Library.Shuffle</c>)
/// that don't receive the engine RNG as a parameter look up the
/// active instance here.
///
/// Populated by whichever orchestrator owns the <see cref="GameRandom"/>
/// (<c>GameDriver</c>, match service, or individual integration tests);
/// reclaimed on teardown. Falls back to <see cref="Default"/> when nothing
/// has been registered for a given player — production code threads its own
/// RNG, tests can override via <see cref="SetDefault"/> for determinism.
///
/// <para>
/// The backing map is <b>not</b> a single process-global static. It lives in
/// an <see cref="AmbientRegistryStore{TStore}"/> scoped per-game via
/// <see cref="GameRegistryScope.PushForGame"/> (installed at game start in
/// <c>GameDriver.RunGameAsync</c>, mirroring <see cref="LogicalClockScope"/>).
/// Concurrent matches in one process see independent maps + independent
/// <see cref="Default"/> slots, so a tutor shuffle can never pick up another
/// live game's RNG. Outside any game scope (direct-construction unit tests)
/// the static API resolves a process-wide fallback store.
/// </para>
///
/// <para>
/// The map is keyed by <see cref="Players.Player.Id"/> rather than the player
/// object because shuffle call sites only see the player ref, not the
/// surrounding game scope; in practice every player in a single game shares
/// the same <see cref="GameRandom"/>, so registering against any one player
/// is sufficient, but per-player keying keeps the surface flexible.
/// </para>
/// </summary>
public static class GameRandomRegistry
{
    /// <summary>Per-game store: the player→RNG map + fallback RNG.</summary>
    public sealed class Store
    {
        internal readonly Dictionary<Guid, GameRandom> ByPlayer = new();
        internal readonly object Lock = new();
        internal GameRandom Default = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>
    /// Install a fresh per-game store as the ambient store for the current
    /// async flow until the returned scope is disposed. Used at game start so
    /// concurrent matches are isolated. See <see cref="GameRegistryScope"/>.
    /// </summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>Process-wide fallback used when no per-player RNG is
    /// registered. Tests that need determinism can call
    /// <see cref="SetDefault"/> before exercising library shuffles.</summary>
    public static GameRandom Default
    {
        get { var s = Current; lock (s.Lock) return s.Default; }
    }

    /// <summary>Replace the active store's fallback RNG.</summary>
    public static void SetDefault(GameRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var s = Current;
        lock (s.Lock) { s.Default = random; }
    }

    /// <summary>Associate <paramref name="random"/> with <paramref name="player"/>.</summary>
    public static void Set(Players.Player player, GameRandom random)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(random);
        var s = Current;
        lock (s.Lock) { s.ByPlayer[player.Id] = random; }
    }

    /// <summary>Return the registered <see cref="GameRandom"/> for
    /// <paramref name="player"/>, falling back to <see cref="Default"/>.</summary>
    public static GameRandom Get(Players.Player? player)
    {
        var s = Current;
        lock (s.Lock)
        {
            if (player is not null && s.ByPlayer.TryGetValue(player.Id, out var rng)) return rng;
            return s.Default;
        }
    }

    /// <summary>Remove the registration for <paramref name="player"/> from the
    /// active store. No-op when nothing was registered.</summary>
    public static void Remove(Players.Player player)
    {
        if (player is null) return;
        var s = Current;
        lock (s.Lock) { s.ByPlayer.Remove(player.Id); }
    }

    /// <summary>Remove all per-player registrations from the active store
    /// (test teardown).</summary>
    public static void Clear()
    {
        var s = Current;
        lock (s.Lock) { s.ByPlayer.Clear(); }
    }
}
