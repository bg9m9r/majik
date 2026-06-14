using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// Thread-safe per-game lookup for the live <see cref="ContinuousEffectsService"/>.
///
/// <para>
/// Effect closures that register a continuous effect (a Layer-2 control
/// change, a P/T buff, …) at <em>resolution time</em> don't receive the
/// per-game <see cref="ContinuousEffectsService"/> as a parameter — the v1
/// sync effect model has no service parameter on
/// <see cref="Majik.Core.Abilities.IEffect.Execute"/>. They look up the live
/// service here and call <see cref="ContinuousEffectsService.Register"/>.
/// </para>
///
/// <para>
/// Mirrors <see cref="Majik.Core.Players.ControlPlayerRegistryProvider"/> /
/// <see cref="Majik.Core.Services.ZoneServiceRegistry"/> /
/// <see cref="Majik.Core.Events.EventBusRegistry"/>: the orchestrator
/// (<see cref="Majik.Core.Game.GameDriver"/>) registers the service at game
/// start (per resolving player + a process-wide default); closures call
/// <see cref="Get"/> at runtime. Returns <see langword="null"/> when nothing
/// is registered — callers fall back to a no-op (suitable for shape /
/// dispatcher-test paths that don't drive a full game), or a supplied test
/// service. Backed by the shared <see cref="AmbientRegistryStore{TStore}"/>
/// so concurrent games stay isolated and finished games reclaim their entries.
/// </para>
/// </summary>
public static class ContinuousEffectsServiceProvider
{
    /// <summary>Per-game store: the player→service map, its default slot, and lock.</summary>
    public sealed class Store
    {
        internal readonly Dictionary<Guid, ContinuousEffectsService> ByPlayer = new();
        internal ContinuousEffectsService? Default;
        internal readonly object Lock = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>Install a fresh per-game store. See <see cref="GameRegistryScope"/>.</summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>Process-wide fallback service, or <see langword="null"/>.</summary>
    public static ContinuousEffectsService? Default
    {
        get { var store = Current; lock (store.Lock) return store.Default; }
    }

    /// <summary>Replace the process-wide fallback service.</summary>
    public static void SetDefault(ContinuousEffectsService? service)
    {
        var store = Current;
        lock (store.Lock) { store.Default = service; }
    }

    /// <summary>Associate <paramref name="service"/> with
    /// <paramref name="player"/> (the resolving player whose effect registers
    /// a continuous effect).</summary>
    public static void Set(Player player, ContinuousEffectsService service)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(service);
        var store = Current;
        lock (store.Lock) { store.ByPlayer[player.Id] = service; }
    }

    /// <summary>Return the service registered for <paramref name="player"/>,
    /// falling back to the process-wide default, or <see langword="null"/>
    /// when neither is set.</summary>
    public static ContinuousEffectsService? Get(Player? player)
    {
        var store = Current;
        lock (store.Lock)
        {
            if (player is not null && store.ByPlayer.TryGetValue(player.Id, out var s)) return s;
            return store.Default;
        }
    }

    /// <summary>Remove the registration for <paramref name="player"/>.</summary>
    public static void Remove(Player player)
    {
        if (player is null) return;
        var store = Current;
        lock (store.Lock) { store.ByPlayer.Remove(player.Id); }
    }

    /// <summary>Remove all registrations (call at game teardown / test cleanup).</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Lock) { store.ByPlayer.Clear(); store.Default = null; }
    }
}
