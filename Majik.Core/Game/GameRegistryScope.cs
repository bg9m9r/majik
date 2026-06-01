using Majik.Core.Events;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;

namespace Majik.Core.Game;

/// <summary>
/// Installs a fresh per-game ambient store for every process-level registry
/// at once — <see cref="AgentRegistry"/>, <see cref="GameRandomRegistry"/>,
/// <see cref="EventBusRegistry"/> and <see cref="ZoneServiceRegistry"/> — for
/// the duration of a game's run.
///
/// <para>
/// Mirrors <see cref="LogicalClockScope"/>: the install happens at the very
/// start of the game's async flow (<c>GameDriver.RunGameAsync</c>, and the
/// single-round <c>GameFacade.StartAsync</c> path), so every effect closure
/// the game resolves — on whatever threadpool thread an <c>await</c>
/// continuation resumes on — reads THIS game's registry stores. Concurrent
/// games run on independent async flows and therefore see independent stores
/// (no cross-game aliasing, no <c>.Default</c> footgun). When the returned
/// scope is disposed at the end of the run, every per-game store is dropped,
/// so the match's entries are reclaimed (no per-match leak).
/// </para>
///
/// <para>
/// The registries' per-player <c>Set</c> calls (made by the driver /
/// facade just after the scope is installed) populate the freshly-minted
/// per-game stores. Outside a scope (most unit tests) the static registry
/// API resolves a process-wide fallback store, so the existing call sites
/// keep working unchanged.
/// </para>
/// </summary>
public static class GameRegistryScope
{
    /// <summary>
    /// Push a fresh per-game store for all four registries. Dispose the
    /// returned handle (typically via <c>using</c>) at the end of the game's
    /// run to restore the previous stores and reclaim this game's entries.
    /// </summary>
    public static IDisposable PushForGame()
    {
        var agents = AgentRegistry.PushScope();
        var rng = GameRandomRegistry.PushScope();
        var bus = EventBusRegistry.PushScope();
        var zones = ZoneServiceRegistry.PushScope();
        return new CompositeScope(agents, rng, bus, zones);
    }

    private sealed class CompositeScope : IDisposable
    {
        private readonly IDisposable[] _scopes;
        private bool _disposed;

        public CompositeScope(params IDisposable[] scopes) => _scopes = scopes;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Dispose in reverse install order so each AsyncLocal restores the
            // store that was active before it was pushed.
            for (var i = _scopes.Length - 1; i >= 0; i--)
            {
                _scopes[i].Dispose();
            }
        }
    }
}
