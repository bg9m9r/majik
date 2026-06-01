using System.Threading;

namespace Majik.Core.Game;

/// <summary>
/// Per-game ambient backing store for the process-level "registries"
/// (<see cref="Majik.Core.Players.Agents.AgentRegistry"/>,
/// <see cref="Majik.Core.Services.ZoneServiceRegistry"/>,
/// <see cref="Majik.Core.Random.GameRandomRegistry"/>,
/// <see cref="Majik.Core.Events.EventBusRegistry"/>).
///
/// <para>
/// Historically each of those registries kept a <c>static</c>
/// <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> shared
/// across the whole process. That single shared map caused three problems
/// with one root cause (a process-global mutable static):
/// </para>
///
/// <list type="number">
///   <item>concurrent matches in one process aliased each other's entries
///   (cross-game corruption — e.g. a tutor shuffling with the wrong game's
///   RNG via the shared <c>.Default</c> footgun);</item>
///   <item>finished matches leaked their entries forever (no per-game
///   teardown actually ran in production);</item>
///   <item>the test suite had to run serially because every class racing on
///   the shared statics flaked.</item>
/// </list>
///
/// <para>
/// This type backs each registry's store with an
/// <see cref="System.Threading.AsyncLocal{T}"/> scope installed for the
/// duration of a game's driver run (see
/// <see cref="GameRegistryScope.PushForGame"/>), mirroring
/// <see cref="LogicalClockScope"/>. The AsyncLocal flows across every
/// <c>await</c> continuation the engine hits, so every effect closure that
/// resolves the active store — on whatever threadpool thread the
/// continuation resumes on — sees THIS game's store. Concurrent games run on
/// independent async flows and therefore see independent stores. When the
/// scope ends the per-game store is dropped, so its entries are reclaimed.
/// </para>
///
/// <para>
/// When no per-game store is installed (the bulk of the unit-test suite
/// constructs effects / agents / zone services directly with no surrounding
/// game), <see cref="Current"/> falls back to a process-wide store. That
/// fallback preserves the old static behaviour for direct-construction paths
/// (so the existing static <c>Get</c>/<c>Set</c> call sites keep working
/// unchanged), while live games get isolation + reclamation.
/// </para>
/// </summary>
/// <typeparam name="TStore">
/// The concrete per-game store type (each registry defines its own, holding
/// its <c>Dictionary&lt;Guid, …&gt;</c> plus any default slot). Must have a
/// public parameterless constructor so a fresh store can be minted per game.
/// </typeparam>
public sealed class AmbientRegistryStore<TStore>
    where TStore : class, new()
{
    private readonly AsyncLocal<TStore?> _ambient = new();

    // Process-wide fallback for construction outside any game scope (most
    // unit tests, and any production call that legitimately runs before a
    // game scope is installed). Shared so cross-object lookups within a test
    // are consistent — exactly the pre-fix static behaviour.
    private readonly TStore _fallback = new();

    /// <summary>
    /// The active store: the per-game store when one is installed for the
    /// current async flow, otherwise the process-wide fallback.
    /// </summary>
    public TStore Current => _ambient.Value ?? _fallback;

    /// <summary>
    /// The process-wide fallback store. Exposed so test-cleanup helpers can
    /// reset it directly (the static registries' <c>Clear()</c> methods do
    /// this) without disturbing any installed per-game scope.
    /// </summary>
    public TStore Fallback => _fallback;

    /// <summary>
    /// Install <paramref name="store"/> as the ambient store for the current
    /// async flow until the returned scope is disposed. Nesting restores the
    /// previous store on dispose.
    /// </summary>
    public IDisposable Push(TStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var previous = _ambient.Value;
        _ambient.Value = store;
        return new Scope(this, previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly AmbientRegistryStore<TStore> _owner;
        private readonly TStore? _previous;
        private bool _disposed;

        public Scope(AmbientRegistryStore<TStore> owner, TStore? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._ambient.Value = _previous;
        }
    }
}
