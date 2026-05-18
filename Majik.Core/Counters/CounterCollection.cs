namespace Majik.Core.Counters;

/// <summary>
/// Mutable counter bag attached to a permanent. Add/Remove/Count by type.
/// Cancellation between +1/+1 and -1/-1 (CR 704.5q) is performed by
/// <see cref="Majik.Core.Rules.StateBasedActions"/>, not here.
/// </summary>
public sealed class CounterCollection
{
    private readonly Dictionary<CounterType, int> _counts = new();

    public int Count(CounterType type) =>
        _counts.TryGetValue(type, out var n) ? n : 0;

    public void Add(CounterType type, int amount = 1)
    {
        if (amount <= 0) return;
        _counts[type] = Count(type) + amount;
    }

    public void Remove(CounterType type, int amount = 1)
    {
        if (amount <= 0) return;
        var cur = Count(type);
        var next = Math.Max(0, cur - amount);
        if (next == 0) _counts.Remove(type);
        else _counts[type] = next;
    }

    public IReadOnlyDictionary<CounterType, int> All => _counts;

    public bool HasAny => _counts.Values.Any(n => n > 0);
}
