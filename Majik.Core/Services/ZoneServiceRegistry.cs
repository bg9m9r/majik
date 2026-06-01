using Majik.Core.Game;

namespace Majik.Core.Services;

/// <summary>
/// Thread-safe per-game <see cref="ZoneService"/> lookup. Effect closures
/// that don't receive a <see cref="ZoneService"/> as a parameter
/// (the v1 sync effect model has no service parameter on
/// <see cref="Majik.Core.Abilities.IEffect.Execute"/>) look up the
/// registered service here.
///
/// Orchestrators register the service at game start; closures call
/// <see cref="Get"/> at runtime. Returns <see langword="null"/> when nothing
/// is registered — callers fall back to raw zone manipulation (which is
/// suitable for shape / dispatcher-test paths that don't need
/// <see cref="Majik.Core.Events.CardMovedEvent"/> publication or
/// <see cref="Majik.Core.Effects.ReplacementBus"/> hooks to fire).
///
/// <para>
/// The backing map is <b>not</b> a single process-global static. It lives in
/// an <see cref="AmbientRegistryStore{TStore}"/> scoped per-game via
/// <see cref="GameRegistryScope.PushForGame"/> (installed at game start in
/// <c>GameDriver.RunGameAsync</c>, mirroring <see cref="LogicalClockScope"/>),
/// so concurrent matches see independent services and a finished match's
/// service is reclaimed when its scope ends. Outside any game scope
/// (direct-construction unit tests) the static API resolves a process-wide
/// fallback store.
/// </para>
///
/// ## Why this exists
///
/// Tutor / fetch effects (Primeval Titan, Scapeshift, fetchlands,
/// Green Sun's Zenith, Chord of Calling, Eldritch Evolution, Search
/// for Tomorrow, etc.) build their resolve closures at card-construction
/// time, but the resolve closure runs much later — after the spell is on
/// the stack and resolving. The single-arg
/// <c>Create(Player owner)</c> dispatcher path (driven by the
/// <see cref="Majik.Core.SourceGen.NamedCardFactoryGenerator"/>) has no
/// <see cref="ZoneService"/> in scope, so the closures previously used
/// raw <c>Zones.Library.RemoveCard</c> + <c>Zones.Battlefield.AddCard</c>
/// mutation. That bypasses
/// <see cref="ZoneService.MoveCard"/>'s
/// <see cref="Majik.Core.Events.CardMovedEvent"/> publication and
/// <see cref="Majik.Core.Effects.ReplacementBus"/> hook — so ETB
/// triggers (bounce-land bounce, Amulet of Vigor untap) and ETB-tapped
/// replacements never fire on the tutored card.
///
/// This registry lets the resolve closure look up the live
/// <see cref="ZoneService"/> at execution time without forcing a
/// signature change on every effect closure or factory call site.
/// </summary>
public static class ZoneServiceRegistry
{
    /// <summary>Per-game store: the player→service map + fallback service.</summary>
    public sealed class Store
    {
        internal readonly Dictionary<Guid, ZoneService> ByPlayer = new();
        internal readonly object Lock = new();
        internal ZoneService? Default;
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>
    /// Install a fresh per-game store as the ambient store for the current
    /// async flow until the returned scope is disposed. Used at game start so
    /// concurrent matches are isolated. See <see cref="GameRegistryScope"/>.
    /// </summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>Process-wide fallback service, or <see langword="null"/>
    /// if none has been registered.</summary>
    public static ZoneService? Default
    {
        get { var s = Current; lock (s.Lock) return s.Default; }
    }

    /// <summary>Replace the active store's fallback service.</summary>
    public static void SetDefault(ZoneService? zoneService)
    {
        var s = Current;
        lock (s.Lock) { s.Default = zoneService; }
    }

    /// <summary>Associate <paramref name="zoneService"/> with <paramref name="player"/>.</summary>
    public static void Set(Players.Player player, ZoneService zoneService)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(zoneService);
        var s = Current;
        lock (s.Lock) { s.ByPlayer[player.Id] = zoneService; }
    }

    /// <summary>Return the registered service for <paramref name="player"/>
    /// (falls back to <see cref="Default"/>), or <see langword="null"/>.</summary>
    public static ZoneService? Get(Players.Player? player)
    {
        var s = Current;
        lock (s.Lock)
        {
            if (player is not null && s.ByPlayer.TryGetValue(player.Id, out var z)) return z;
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
        lock (s.Lock)
        {
            s.ByPlayer.Clear();
            s.Default = null;
        }
    }
}
