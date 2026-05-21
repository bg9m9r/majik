using System.Runtime.CompilerServices;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Thread-safe map from <see cref="Player"/> to <see cref="IPlayerAgent"/>.
/// Effect closures that can't receive an agent as a parameter (the v1 sync
/// effect model has no async/agent parameter) look up the owning agent here.
///
/// Populated at game-start by whatever orchestrates the match (MatchService,
/// integration tests, etc.). Cleared when the game tears down.
/// </summary>
public static class AgentRegistry
{
    private static readonly Dictionary<Guid, IPlayerAgent> _agents = new();
    private static readonly object _lock = new();

    /// <summary>Associate <paramref name="agent"/> with <paramref name="player"/>.</summary>
    public static void Set(Player player, IPlayerAgent agent)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(agent);
        lock (_lock) { _agents[player.Id] = agent; }
    }

    /// <summary>Return the agent registered for <paramref name="player"/>, or
    /// <see langword="null"/> if none has been registered.</summary>
    public static IPlayerAgent? Get(Player player)
    {
        if (player is null) return null;
        lock (_lock) { return _agents.TryGetValue(player.Id, out var a) ? a : null; }
    }

    /// <summary>Remove all registrations (call at game teardown / test cleanup).</summary>
    public static void Clear()
    {
        lock (_lock) { _agents.Clear(); }
    }
}
