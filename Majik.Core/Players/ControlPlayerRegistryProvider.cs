using Majik.Core.Game;

namespace Majik.Core.Players;

/// <summary>
/// Thread-safe per-game lookup for the live <see cref="ControlPlayerRegistry"/>.
///
/// Effect closures that take control of another player (Mindslaver's
/// activated ability, Emrakul, the Promised End's cast trigger) don't receive
/// the per-game <see cref="ControlPlayerRegistry"/> as a parameter — the v1
/// sync effect model has no service parameter on
/// <see cref="Majik.Core.Abilities.IEffect.Execute"/>. They look up the live
/// registry here at resolution time and call
/// <see cref="ControlPlayerRegistry.GrantControl"/>.
///
/// Mirrors <see cref="Agents.AgentRegistry"/> /
/// <see cref="Majik.Core.Services.ZoneServiceRegistry"/> /
/// <see cref="Majik.Core.Events.EventBusRegistry"/>: the orchestrator
/// (<see cref="Majik.Core.Game.GameDriver"/>) registers the registry at game
/// start; closures call <see cref="Get"/> at runtime. Returns
/// <see langword="null"/> when nothing is registered — callers fall back to a
/// no-op (suitable for shape / dispatcher-test paths that don't drive a full
/// game), or a supplied test sink.
/// </summary>
public static class ControlPlayerRegistryProvider
{
    /// <summary>Per-game store: the player→registry map, its default slot, and lock.</summary>
    public sealed class Store
    {
        internal readonly Dictionary<Guid, ControlPlayerRegistry> ByPlayer = new();
        internal ControlPlayerRegistry? Default;
        internal readonly object Lock = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>Install a fresh per-game store. See <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>Process-wide fallback registry, or <see langword="null"/> if
    /// none has been registered.</summary>
    public static ControlPlayerRegistry? Default
    {
        get { var store = Current; lock (store.Lock) return store.Default; }
    }

    /// <summary>Replace the process-wide fallback registry.</summary>
    public static void SetDefault(ControlPlayerRegistry? registry)
    {
        var store = Current;
        lock (store.Lock) { store.Default = registry; }
    }

    /// <summary>Associate <paramref name="registry"/> with
    /// <paramref name="player"/> (the resolving player whose effect takes
    /// control).</summary>
    public static void Set(Player player, ControlPlayerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(registry);
        var store = Current;
        lock (store.Lock) { store.ByPlayer[player.Id] = registry; }
    }

    /// <summary>Return the registry registered for <paramref name="player"/>,
    /// falling back to the process-wide default, or <see langword="null"/>
    /// when neither is set.</summary>
    public static ControlPlayerRegistry? Get(Player? player)
    {
        var store = Current;
        lock (store.Lock)
        {
            if (player is not null && store.ByPlayer.TryGetValue(player.Id, out var r)) return r;
            return store.Default;
        }
    }

    /// <summary>Remove the registration for <paramref name="player"/>. No-op
    /// when nothing was registered.</summary>
    public static void Remove(Player player)
    {
        if (player is null) return;
        var store = Current;
        lock (store.Lock) { store.ByPlayer.Remove(player.Id); }
    }

    /// <summary>Remove all registrations (call at game teardown / test
    /// cleanup).</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Lock) { store.ByPlayer.Clear(); store.Default = null; }
    }
}
