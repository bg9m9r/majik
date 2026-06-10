namespace Majik.Bot.OpponentModel;

/// <summary>
/// Allocates K determinized worlds across the belief's top-M archetypes proportional to
/// weight, using the largest-remainder (Hamilton) method so the world counts sum exactly
/// to K and no deserving archetype is starved by rounding. Drops the long tail (top-M cap)
/// so a smeared belief doesn't spend a world on noise.
/// </summary>
public static class WorldAllocator
{
    public static IReadOnlyList<(string Archetype, int Worlds)> Allocate(
        IReadOnlyList<ArchetypeWeight> belief, int k, int topM)
    {
        if (k <= 0) return System.Array.Empty<(string, int)>();
        var top = belief.OrderByDescending(b => b.Weight).Take(topM).ToList();
        if (top.Count == 0) return System.Array.Empty<(string, int)>();
        var mass = top.Sum(b => b.Weight);
        if (mass <= 0) return new[] { (top[0].Archetype, k) };

        var alloc = top.Select(b =>
        {
            double exact = b.Weight / mass * k;
            int floor = (int)Math.Floor(exact);
            return (b.Archetype, worlds: floor, frac: exact - floor);
        }).ToList();

        int assigned = alloc.Sum(a => a.worlds);
        var byFrac = alloc.Select((a, i) => (i, a.frac)).OrderByDescending(t => t.frac).ToList();
        for (int r = 0; r < k - assigned; r++)
        {
            int idx = byFrac[r % byFrac.Count].i;
            var cur = alloc[idx];
            alloc[idx] = (cur.Archetype, cur.worlds + 1, cur.frac);
        }
        return alloc.Where(a => a.worlds > 0).Select(a => (a.Archetype, a.worlds)).ToList();
    }
}
