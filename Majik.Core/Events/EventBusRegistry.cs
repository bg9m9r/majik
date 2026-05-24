namespace Majik.Core.Events;

/// <summary>
/// Thread-safe per-game <see cref="IEventBus"/> lookup. Effect
/// closures that don't take an <see cref="IEventBus"/> as a parameter
/// (e.g. tutor factories publishing <see cref="LibraryShuffledEvent"/>)
/// look up the registered bus here.
///
/// Mirrors <see cref="Majik.Core.Players.Agents.AgentRegistry"/> /
/// <see cref="Majik.Core.Random.GameRandomRegistry"/>: orchestrators
/// register the bus at game start; closures call <see cref="Get"/>
/// at runtime. Returns <see langword="null"/> when nothing is
/// registered — callers treat publishing as best-effort.
/// </summary>
public static class EventBusRegistry
{
    private static readonly Dictionary<Guid, IEventBus> _byPlayer = new();
    private static readonly object _lock = new();
    private static IEventBus? _default;

    /// <summary>Process-wide fallback bus, or <see langword="null"/>
    /// if none has been registered.</summary>
    public static IEventBus? Default
    {
        get { lock (_lock) return _default; }
    }

    /// <summary>Replace the process-wide fallback bus.</summary>
    public static void SetDefault(IEventBus? bus)
    {
        lock (_lock) { _default = bus; }
    }

    /// <summary>Associate <paramref name="bus"/> with <paramref name="player"/>.</summary>
    public static void Set(Players.Player player, IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(bus);
        lock (_lock) { _byPlayer[player.Id] = bus; }
    }

    /// <summary>Return the registered bus for <paramref name="player"/>
    /// (falls back to <see cref="Default"/>), or <see langword="null"/>.</summary>
    public static IEventBus? Get(Players.Player? player)
    {
        lock (_lock)
        {
            if (player is not null && _byPlayer.TryGetValue(player.Id, out var bus)) return bus;
            return _default;
        }
    }

    /// <summary>Remove all per-player registrations (test teardown).</summary>
    public static void Clear()
    {
        lock (_lock) { _byPlayer.Clear(); }
    }
}
