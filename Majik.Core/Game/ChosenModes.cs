namespace Majik.Core.Game;

/// <summary>CR 700.2 — the modes a modal spell resolves.</summary>
public static class ChosenModes
{
    /// <summary>
    /// The announced modes that count: valid indices, each at most once
    /// (CR 700.2d), the first <paramref name="maxModes"/> of them, in PRINTED
    /// order (CR 608.2c: a spell's instructions are followed in the order
    /// written), whatever order they were announced in.
    /// </summary>
    public static IEnumerable<int> InPrintedOrder(IEnumerable<int> announced, int modeCount, int maxModes) =>
        announced
            .Where(i => i >= 0 && i < modeCount)
            .Distinct()
            .Take(maxModes)
            .OrderBy(i => i);
}