namespace Majik.Core.Counters;

/// <summary>
/// Mutable counter bag attached to a permanent. Add/Remove/Count by type.
/// Cancellation between +1/+1 and -1/-1 (CR 704.5q) is performed by
/// <see cref="Majik.Core.Rules.StateBasedActions"/>, not here.
/// </summary>
public sealed class CounterCollection
{
    private readonly Dictionary<CounterType, int> _counts = new();

    /// <summary>
    /// CR 613 — optional hook the owning <see cref="Majik.Core.Cards.Permanent"/>
    /// wires to invalidate the continuous-effects memoization cache whenever
    /// the counter bag changes. The +1/+1 / -1/-1 P/T arithmetic is re-applied
    /// per-Compute (so it needs no invalidation), but counter COUNTS also gate
    /// other layered effects (Pelt Collector's trample at 3+ counters, etc.),
    /// which the cache must refresh. Null until wired; a no-op when unset.
    /// </summary>
    public Action? OnMutated { get; set; }

    public int Count(CounterType type) =>
        _counts.TryGetValue(type, out var n) ? n : 0;

    public void Add(CounterType type, int amount = 1)
    {
        if (amount <= 0) return;
        _counts[type] = Count(type) + amount;
        OnMutated?.Invoke();
    }

    public void Remove(CounterType type, int amount = 1)
    {
        if (amount <= 0) return;
        var cur = Count(type);
        var next = Math.Max(0, cur - amount);
        if (next == 0) _counts.Remove(type);
        else _counts[type] = next;
        OnMutated?.Invoke();
    }

    public IReadOnlyDictionary<CounterType, int> All => _counts;

    public bool HasAny => _counts.Values.Any(n => n > 0);
}
