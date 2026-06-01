using Majik.Core.Game;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Thread-safe map from <see cref="Player"/> to <see cref="IPlayerAgent"/>.
/// Effect closures that can't receive an agent as a parameter (the v1 sync
/// effect model has no async/agent parameter) look up the owning agent here.
///
/// Populated at game-start by whatever orchestrates the match (MatchService,
/// integration tests, etc.). Cleared when the game tears down.
///
/// <para>
/// The backing map is <b>not</b> a single process-global static. It lives in
/// an <see cref="AmbientRegistryStore{TStore}"/> scoped per-game via
/// <see cref="GameRegistryScope.PushForGame"/> (installed at game start in
/// <c>GameDriver.RunGameAsync</c> / <c>GameFacade</c>, mirroring
/// <see cref="LogicalClockScope"/>). Concurrent matches in one process see
/// independent maps, and a finished match's entries are reclaimed when its
/// scope ends. Outside any game scope (direct-construction unit tests) the
/// static API resolves a process-wide fallback store, so the existing
/// <c>Set</c>/<c>Get</c> call sites keep working unchanged.
/// </para>
/// </summary>
public static class AgentRegistry
{
    /// <summary>Per-game store: the player→agent map guarded by its own lock.</summary>
    public sealed class Store
    {
        internal readonly Dictionary<Guid, IPlayerAgent> Agents = new();
        internal readonly object Lock = new();
    }

    private static readonly AmbientRegistryStore<Store> _ambient = new();

    private static Store Current => _ambient.Current;

    /// <summary>
    /// Install a fresh per-game store as the ambient store for the current
    /// async flow until the returned scope is disposed. Used at game start so
    /// concurrent matches are isolated. See <see cref="GameRegistryScope"/>.
    /// </summary>
    public static IDisposable PushScope() => _ambient.Push(new Store());

    /// <summary>Associate <paramref name="agent"/> with <paramref name="player"/>.</summary>
    public static void Set(Player player, IPlayerAgent agent)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(agent);
        var store = Current;
        lock (store.Lock) { store.Agents[player.Id] = agent; }
    }

    /// <summary>Return the agent registered for <paramref name="player"/>, or
    /// <see langword="null"/> if none has been registered.</summary>
    public static IPlayerAgent? Get(Player player)
    {
        if (player is null) return null;
        var store = Current;
        lock (store.Lock) { return store.Agents.TryGetValue(player.Id, out var a) ? a : null; }
    }

    /// <summary>Remove the registration for <paramref name="player"/>. No-op
    /// when nothing was registered. Per-player removal is safer than
    /// <see cref="Clear"/> when multiple games share the same fallback store
    /// (each <see cref="Player"/> has a unique Guid Id, so the store can hold
    /// entries for several matches at once — clearing would rip out everyone
    /// else's seats).</summary>
    public static void Remove(Player player)
    {
        if (player is null) return;
        var store = Current;
        lock (store.Lock) { store.Agents.Remove(player.Id); }
    }

    /// <summary>Remove all registrations from the active store (call at game
    /// teardown / test cleanup).</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Lock) { store.Agents.Clear(); }
    }
}
