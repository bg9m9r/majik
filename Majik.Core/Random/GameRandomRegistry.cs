namespace Majik.Core.Random;

/// <summary>
/// Thread-safe per-game <see cref="GameRandom"/> registry. Effect
/// closures (e.g. tutor factories invoking <c>Library.Shuffle</c>)
/// that don't receive the engine RNG as a parameter look up the
/// active instance here.
///
/// Mirrors <see cref="Majik.Core.Players.Agents.AgentRegistry"/> in
/// spirit: populated by whichever orchestrator owns the
/// <see cref="GameRandom"/> (<c>GameDriver</c>, match service, or
/// individual integration tests); cleared on teardown. Falls back to
/// <see cref="Default"/> when nothing has been registered for a
/// given player — production code threads its own RNG, tests can
/// override via <see cref="SetDefault"/> for determinism.
///
/// The registry is keyed by <see cref="Players.Player.Id"/> rather
/// than the player object because shuffle call sites only see the
/// player ref, not the surrounding game scope; in practice every
/// player in a single game shares the same <see cref="GameRandom"/>,
/// so registering against any one player is sufficient, but
/// per-player keying keeps the surface flexible.
/// </summary>
public static class GameRandomRegistry
{
    private static readonly Dictionary<Guid, GameRandom> _byPlayer = new();
    private static readonly object _lock = new();
    private static GameRandom _default = new();

    /// <summary>Process-wide fallback used when no per-player RNG is
    /// registered. Tests that need determinism can call
    /// <see cref="SetDefault"/> before exercising library shuffles.</summary>
    public static GameRandom Default
    {
        get { lock (_lock) return _default; }
    }

    /// <summary>Replace the process-wide fallback.</summary>
    public static void SetDefault(GameRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        lock (_lock) { _default = random; }
    }

    /// <summary>Associate <paramref name="random"/> with <paramref name="player"/>.</summary>
    public static void Set(Players.Player player, GameRandom random)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(random);
        lock (_lock) { _byPlayer[player.Id] = random; }
    }

    /// <summary>Return the registered <see cref="GameRandom"/> for
    /// <paramref name="player"/>, falling back to <see cref="Default"/>.</summary>
    public static GameRandom Get(Players.Player? player)
    {
        lock (_lock)
        {
            if (player is not null && _byPlayer.TryGetValue(player.Id, out var rng)) return rng;
            return _default;
        }
    }

    /// <summary>Remove all per-player registrations (test teardown).</summary>
    public static void Clear()
    {
        lock (_lock) { _byPlayer.Clear(); }
    }
}
