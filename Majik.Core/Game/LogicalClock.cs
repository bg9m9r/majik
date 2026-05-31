namespace Majik.Core.Game;

/// <summary>
/// CR 613.7 / 603.3b — a per-game <b>monotonic logical clock</b> that assigns
/// the ORDER-DETERMINING timestamps the engine relies on (trigger APNAP
/// ordering, continuous-effect layer ordering, legend-rule / planeswalker-
/// uniqueness "which one entered first", and the relative ordering of game
/// events consumed by delayed-trigger fences).
///
/// <para>
/// Historically these timestamps were wall-clock <see cref="DateTime.UtcNow"/>
/// reads taken at object-construction time. Wall-clock is NOT reproducible:
/// the same (seed, command-order) replay can produce different absolute
/// instants and — worse — different RELATIVE orderings when two constructions
/// land in the same OS tick. That non-determinism is the load-bearing blocker
/// for replay / rehydration (PLAN 08).
/// </para>
///
/// <para>
/// The logical clock replaces those reads with a strictly-increasing counter
/// owned by the game. Because the counter is bumped at the exact same
/// construction points wall-clock was read, it assigns timestamps in the SAME
/// order construction currently happens — so it is behaviour-preserving for
/// every existing ordering scenario while being fully reproducible.
/// </para>
///
/// <para>
/// The clock still hands out <see cref="DateTime"/> values (a fixed epoch plus
/// the counter as ticks) so every existing <c>OrderBy(x =&gt; x.Timestamp)</c>
/// and <c>e.Timestamp &gt; resolvedAt</c> comparison keeps working unchanged;
/// only the SOURCE of the value moves from wall-clock to the counter.
/// </para>
/// </summary>
public interface ILogicalClock
{
    /// <summary>Next strictly-increasing logical order value (1, 2, 3, …).</summary>
    long NextOrder();

    /// <summary>
    /// Next order value projected onto a <see cref="DateTime"/> (a fixed epoch
    /// plus <see cref="NextOrder"/> ticks). Strictly increasing, so existing
    /// timestamp comparisons preserve construction order.
    /// </summary>
    DateTime NextTimestamp();
}

/// <summary>
/// Default <see cref="ILogicalClock"/>: a thread-safe, strictly-increasing
/// counter projected onto <see cref="DateTime"/> values from a fixed epoch.
/// </summary>
public sealed class LogicalClock : ILogicalClock
{
    // A fixed, arbitrary epoch well clear of DateTime.MinValue so the first
    // few orders don't underflow and well clear of "now" so logical
    // timestamps never collide with any wall-clock value a stray UtcNow read
    // might still produce elsewhere. 1 tick == 1 order increment.
    internal static readonly DateTime Epoch =
        new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private long _counter;

    public long NextOrder() => System.Threading.Interlocked.Increment(ref _counter);

    public DateTime NextTimestamp() => Epoch.AddTicks(NextOrder());
}

/// <summary>
/// Ambient accessor for the active per-game <see cref="ILogicalClock"/>.
///
/// <para>
/// The clock is installed for the duration of a game's driver run via
/// <see cref="Push"/> (an <see cref="System.Threading.AsyncLocal{T}"/> scope
/// that flows across the engine's <c>await</c> continuations, so every object
/// constructed while the game advances — on any threadpool thread the
/// continuation resumes on — sees the SAME game's clock). Concurrent games run
/// on independent async flows and therefore see independent clocks.
/// </para>
///
/// <para>
/// When no per-game clock is installed (the bulk of the unit-test suite
/// constructs <c>TriggeredAbility</c> / <c>ContinuousEffect</c> / permanents
/// directly with no surrounding game), <see cref="Current"/> falls back to a
/// process-wide monotonic clock. That fallback is still strictly increasing,
/// so it preserves — and slightly hardens — the "later construction sorts
/// later" invariant the old <see cref="DateTime.UtcNow"/> reads provided,
/// without the same-OS-tick ties UtcNow could produce.
/// </para>
/// </summary>
public static class LogicalClockScope
{
    private static readonly System.Threading.AsyncLocal<ILogicalClock?> _ambient = new();

    // Process-wide fallback for construction outside any game scope (most unit
    // tests). Monotonic so ordering still holds; shared so cross-object order
    // within a test is consistent.
    private static readonly LogicalClock _fallback = new();

    /// <summary>
    /// The active logical clock: the per-game clock when one is installed,
    /// otherwise the process-wide monotonic fallback.
    /// </summary>
    public static ILogicalClock Current => _ambient.Value ?? _fallback;

    /// <summary>
    /// Install <paramref name="clock"/> as the ambient clock for the current
    /// async flow until the returned scope is disposed. Nesting restores the
    /// previous clock on dispose.
    /// </summary>
    public static IDisposable Push(ILogicalClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var previous = _ambient.Value;
        _ambient.Value = clock;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly ILogicalClock? _previous;
        private bool _disposed;

        public Scope(ILogicalClock? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ambient.Value = _previous;
        }
    }
}
