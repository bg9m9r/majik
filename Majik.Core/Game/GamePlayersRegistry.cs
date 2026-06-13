using Majik.Core.Players;

namespace Majik.Core.Game;

/// <summary>
/// Ambient per-game accessor for the live player set, so resolution paths that
/// have no <see cref="GameContext"/> / <see cref="Abilities.ResolutionContext"/>
/// can still read "each opponent" (CR 102.4).
///
/// <para>
/// Mana abilities resolve IMMEDIATELY and never use the stack (CR 605.3), so —
/// unlike a triggered/activated ability that threads a
/// <see cref="Abilities.ResolutionContext"/> through <c>ResolveAsync</c> — a
/// mana ability's additional-cost payer (Grove of the Burnwillows'
/// "Each opponent gains 1 life") has no resolution context to read the live
/// game off. Before this registry, the Grove rider rode a build-time
/// <c>opponentResolver</c> that the production binder path
/// (<see cref="Majik.Core.CardData.OracleManaBinder"/>) never supplied — it was
/// inert in real games. This registry is the mana-ability analogue of
/// <see cref="Majik.Core.CardData.Factories.ContextOpponents"/>: it reads the
/// opponent set from live game state at activation time instead.
/// </para>
///
/// <para>
/// Backed by an <see cref="AmbientRegistryStore{TStore}"/> scoped per-game via
/// <see cref="GameRegistryScope.PushForGame"/> (installed at game start in
/// <c>GameDriver.RunGameAsync</c> / <c>GameFacade</c>, mirroring
/// <see cref="Majik.Core.Players.Agents.AgentRegistry"/>). The driver/facade
/// call <see cref="Set"/> with the seated players just after the scope is
/// installed. Concurrent matches in one process see independent player sets;
/// a finished match's entry is reclaimed when its scope ends. Outside any game
/// scope (direct-construction unit tests) <see cref="AllPlayers"/> resolves the
/// process-wide fallback store (empty unless the test installs a scope), so
/// shape-only mana-ability paths are a safe no-op rather than throwing.
/// </para>
/// </summary>
public static class GamePlayersRegistry
{
    /// <summary>Per-game store: the live player list for the active match.</summary>
    public sealed class Store
    {
        internal IReadOnlyList<Player> Players = System.Array.Empty<Player>();
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

    /// <summary>Register the live seated players for the active game.</summary>
    public static void Set(IReadOnlyList<Player> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        var store = Current;
        lock (store.Lock) { store.Players = players; }
    }

    /// <summary>
    /// Every player currently in the active game, or an empty list when no
    /// player set has been registered (shape-only paths). Never null.
    /// </summary>
    public static IReadOnlyList<Player> AllPlayers
    {
        get
        {
            var store = Current;
            lock (store.Lock) { return store.Players; }
        }
    }

    /// <summary>
    /// CR 102.1 / 102.4 — every opponent of <paramref name="controller"/> still
    /// in the game (not the controller, not a player who has left). The
    /// mana-ability analogue of
    /// <see cref="Majik.Core.CardData.Factories.ContextOpponents.Of"/>: reads the
    /// live player set off this ambient registry at activation. Returns an empty
    /// sequence when no player set is installed, so an "each opponent" mana-
    /// ability rider is a safe no-op on shape-only paths.
    /// </summary>
    public static IEnumerable<Player> OpponentsOf(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (var p in AllPlayers)
        {
            if (p == null) continue;
            // CR 102.1 — a player is never their own opponent.
            if (ReferenceEquals(p, controller)) continue;
            // CR 800.4a — a player who has left the game is no longer an opponent.
            if (p.HasLost) continue;
            yield return p;
        }
    }

    /// <summary>Reset the active store (test cleanup).</summary>
    public static void Clear()
    {
        var store = Current;
        lock (store.Lock) { store.Players = System.Array.Empty<Player>(); }
    }
}
