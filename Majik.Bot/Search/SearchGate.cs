using System.Diagnostics;

namespace Majik.Bot.Search;

/// <summary>
/// Bounded-wait concurrency gate for top-level LIVE MCTS searches.
///
/// <para>
/// <b>Why:</b> production runs on 1 vCPU. Two overlapping ~1.5 s CPU-bound
/// searches split the core, so each completes fewer iterations → weaker
/// decisions AND degraded API latency. Gated searches QUEUE instead: every
/// search runs at full strength, and a queued bot just "thinks" slightly
/// longer — invisible against human reaction times.
/// </para>
///
/// <para>
/// <b>Placement:</b> held only around the top-level decision searches in
/// <see cref="SearchStrategy"/> (<c>PickAttackers</c> /
/// <c>PickPriorityAction</c> — wherever <c>SearchRoot</c> runs). NEVER inside
/// <see cref="EngineSimulator"/> rollouts: those are nested within a search
/// that already holds the permit, and gating them would self-deadlock.
/// </para>
///
/// <para>
/// <b>Starvation guard:</b> <see cref="TryEnter"/> waits at most the
/// configured timeout. On timeout the caller falls back to its heuristic
/// decision for that pick (the established fallback pattern in
/// <see cref="SearchStrategy"/>) instead of stalling the match indefinitely.
/// </para>
///
/// <para>
/// The in-flight / max-observed counters are cheap Interlocked instrumentation
/// used by tests to PROVE serialization (max in-flight == 1) without timing
/// assertions; they are not consulted on any decision path.
/// </para>
/// </summary>
internal sealed class SearchGate
{
    /// <summary>
    /// Default bounded wait. Generous vs the ~1.5 s production search budget
    /// (several queued searches still clear well within it) but finite, so a
    /// wedged permit degrades ONE pick to the heuristic instead of freezing
    /// the match.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _sem;
    private readonly TimeSpan _timeout;
    private int _inFlight;
    private int _maxObserved;
    private int _enterCount;

    public SearchGate(int permits, TimeSpan timeout)
    {
        if (permits < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(permits), permits, "SearchGate needs at least one permit.");
        }

        Permits = permits;
        _timeout = timeout;
        _sem = new SemaphoreSlim(permits, permits);
    }

    /// <summary>Configured permit count (immutable; see <see cref="SearchConcurrencyGate"/>).</summary>
    public int Permits { get; }

    /// <summary>Total successful entries (test instrumentation).</summary>
    public int EnterCount => Volatile.Read(ref _enterCount);

    /// <summary>Highest concurrent-holder count ever observed (test instrumentation).</summary>
    public int MaxObservedConcurrency => Volatile.Read(ref _maxObserved);

    /// <summary>
    /// Acquire a permit, waiting at most the configured timeout. Returns false
    /// on timeout (caller must fall back to its heuristic decision and must NOT
    /// call <see cref="Exit"/>). Never throws on the timeout path.
    /// </summary>
    public bool TryEnter()
    {
        if (!_sem.Wait(_timeout))
        {
            Trace.TraceWarning(
                "SearchGate: search permit not acquired within {0:N0} ms — " +
                "falling back to the heuristic decision for this pick.",
                _timeout.TotalMilliseconds);
            return false;
        }

        Interlocked.Increment(ref _enterCount);
        var now = Interlocked.Increment(ref _inFlight);
        // CAS-max so MaxObservedConcurrency is exact under contention.
        int seen;
        while (now > (seen = Volatile.Read(ref _maxObserved)) &&
               Interlocked.CompareExchange(ref _maxObserved, now, seen) != seen)
        {
            // retry — another thread raced the max update
        }

        return true;
    }

    /// <summary>Release a permit previously acquired via a successful <see cref="TryEnter"/>.</summary>
    public void Exit()
    {
        Interlocked.Decrement(ref _inFlight);
        _sem.Release();
    }
}

/// <summary>
/// Process-wide holder for the shared <see cref="SearchGate"/> used by every
/// strategy whose <see cref="BotConfig.SearchConcurrency"/> is non-null.
///
/// <para>
/// <b>Semantics — first-configured value wins (documented):</b> the first call
/// to <see cref="Shared"/> creates the gate with that permit count; later calls
/// with a DIFFERENT count log a trace warning and reuse the existing gate.
/// Rationale: in production every live bot is built from the same
/// <c>ServerBotOptions</c>, so configs never actually disagree; splitting
/// disagreeing configs across multiple semaphores would silently defeat the
/// gate's whole purpose (bounding total concurrent search CPU).
/// </para>
///
/// <para>
/// Strategies with a null <see cref="BotConfig.SearchConcurrency"/> (the
/// default — unit tests, strength probes, sim-internal agents) never touch
/// this holder and remain fully ungated.
/// </para>
/// </summary>
internal static class SearchConcurrencyGate
{
    private static readonly object Lock = new();
    private static SearchGate? _shared;

    /// <summary>
    /// Get (or lazily create) the process-wide gate. First-configured permit
    /// count wins; a disagreeing later value logs and reuses the existing gate.
    /// </summary>
    public static SearchGate Shared(int permits)
    {
        lock (Lock)
        {
            if (_shared is null)
            {
                _shared = new SearchGate(permits, SearchGate.DefaultTimeout);
            }
            else if (_shared.Permits != permits)
            {
                Trace.TraceWarning(
                    "SearchConcurrencyGate: requested {0} permits but the process-wide " +
                    "gate was already configured with {1} — first-configured value wins.",
                    permits, _shared.Permits);
            }

            return _shared;
        }
    }
}
