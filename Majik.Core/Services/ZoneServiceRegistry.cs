namespace Majik.Core.Services;

/// <summary>
/// Thread-safe per-game <see cref="ZoneService"/> lookup. Effect closures
/// that don't receive a <see cref="ZoneService"/> as a parameter
/// (the v1 sync effect model has no service parameter on
/// <see cref="Majik.Core.Abilities.IEffect.Execute"/>) look up the
/// registered service here.
///
/// Mirrors <see cref="Majik.Core.Players.Agents.AgentRegistry"/> /
/// <see cref="Majik.Core.Random.GameRandomRegistry"/> /
/// <see cref="Majik.Core.Events.EventBusRegistry"/>: orchestrators
/// register the service at game start; closures call <see cref="Get"/>
/// at runtime. Returns <see langword="null"/> when nothing is
/// registered — callers fall back to raw zone manipulation (which is
/// suitable for shape / dispatcher-test paths that don't need
/// <see cref="Majik.Core.Events.CardMovedEvent"/> publication or
/// <see cref="Majik.Core.Effects.ReplacementBus"/> hooks to fire).
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
    private static readonly Dictionary<Guid, ZoneService> _byPlayer = new();
    private static readonly object _lock = new();
    private static ZoneService? _default;

    /// <summary>Process-wide fallback service, or <see langword="null"/>
    /// if none has been registered.</summary>
    public static ZoneService? Default
    {
        get { lock (_lock) return _default; }
    }

    /// <summary>Replace the process-wide fallback service.</summary>
    public static void SetDefault(ZoneService? zoneService)
    {
        lock (_lock) { _default = zoneService; }
    }

    /// <summary>Associate <paramref name="zoneService"/> with <paramref name="player"/>.</summary>
    public static void Set(Players.Player player, ZoneService zoneService)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(zoneService);
        lock (_lock) { _byPlayer[player.Id] = zoneService; }
    }

    /// <summary>Return the registered service for <paramref name="player"/>
    /// (falls back to <see cref="Default"/>), or <see langword="null"/>.</summary>
    public static ZoneService? Get(Players.Player? player)
    {
        lock (_lock)
        {
            if (player is not null && _byPlayer.TryGetValue(player.Id, out var z)) return z;
            return _default;
        }
    }

    /// <summary>Remove all per-player registrations (test teardown).</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _byPlayer.Clear();
            _default = null;
        }
    }
}
