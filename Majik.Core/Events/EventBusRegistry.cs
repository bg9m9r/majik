using Majik.Core.Game;

namespace Majik.Core.Events;

/// <summary>
/// Thread-safe per-game <see cref="IEventBus"/> lookup. Effect
/// closures that don't take an <see cref="IEventBus"/> as a parameter
/// (e.g. tutor factories publishing <see cref="LibraryShuffledEvent"/>)
/// look up the registered bus here.
///
/// Orchestrators register the bus at game start; closures call
/// <see cref="Get"/> at runtime. Returns <see langword="null"/> when nothing
/// is registered — callers treat publishing as best-effort.
///
/// <para>
/// The backing map is <b>not</b> a single process-global static. It lives in
/// an <see cref="AmbientRegistryStore{TStore}"/> scoped per-game via
/// <see cref="GameRegistryScope.PushForGame"/> (installed at game start in
/// <c>GameDriver.RunGameAsync</c>, mirroring <see cref="LogicalClockScope"/>),
/// so concurrent matches see independent buses and a finished match's bus is
/// reclaimed when its scope ends. Outside any game scope (direct-construction
/// unit tests) the static API resolves a process-wide fallback store.
/// </para>
/// </summary>
public static class EventBusRegistry
{
    /// <summary>Per-game store: the player→bus map + fallback bus.</summary>
    public sealed class Store
    {
        internal readonly Dictionary<Guid, IEventBus> ByPlayer = new();
        internal readonly object Lock = new();
        internal IEventBus? Default;
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>
    /// Install a fresh per-game store as the ambient store for the current
    /// async flow until the returned scope is disposed. Used at game start so
    /// concurrent matches are isolated. See <see cref="GameRegistryScope"/>.
    /// </summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>Process-wide fallback bus, or <see langword="null"/>
    /// if none has been registered.</summary>
    public static IEventBus? Default
    {
        get { var s = Current; lock (s.Lock) return s.Default; }
    }

    /// <summary>Replace the active store's fallback bus.</summary>
    public static void SetDefault(IEventBus? bus)
    {
        var s = Current;
        lock (s.Lock) { s.Default = bus; }
    }

    /// <summary>Associate <paramref name="bus"/> with <paramref name="player"/>.</summary>
    public static void Set(Players.Player player, IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(bus);
        var s = Current;
        lock (s.Lock) { s.ByPlayer[player.Id] = bus; }
    }

    /// <summary>Return the registered bus for <paramref name="player"/>
    /// (falls back to <see cref="Default"/>), or <see langword="null"/>.</summary>
    public static IEventBus? Get(Players.Player? player)
    {
        var s = Current;
        lock (s.Lock)
        {
            if (player is not null && s.ByPlayer.TryGetValue(player.Id, out var bus)) return bus;
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
        lock (s.Lock) { s.ByPlayer.Clear(); s.Default = null; }
    }
}
