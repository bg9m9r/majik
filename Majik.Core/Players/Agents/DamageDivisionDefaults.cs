namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 601.2d / CR 119.4 — shared helpers for the divide-damage prompt
/// (<see cref="IPlayerAgent.ChooseDamageDivisionAsync"/>). Both the agent's
/// default split and <see cref="Majik.Core.Game.SpellCastFlow"/>'s defensive
/// normalisation of an agent-supplied split route through here so the two never
/// diverge.
/// </summary>
public static class DamageDivisionDefaults
{
    /// <summary>
    /// CR 119.4 — even split of <paramref name="totalDamage"/> across
    /// <paramref name="targetCount"/> targets, with the remainder front-loaded
    /// onto the earliest targets (3 among two → [2, 1]; 5 among three →
    /// [2, 2, 1]). Each target gets at least 1 whenever
    /// <paramref name="totalDamage"/> ≥ <paramref name="targetCount"/> (which
    /// the divided-damage cap word guarantees — you can't choose more targets
    /// than the printed damage). Returns an empty list when there are no
    /// targets.
    /// </summary>
    public static IReadOnlyList<int> EvenSplit(int totalDamage, int targetCount)
    {
        if (targetCount <= 0) return System.Array.Empty<int>();
        var result = new int[targetCount];
        var baseShare = totalDamage / targetCount;
        var remainder = totalDamage % targetCount;
        for (var i = 0; i < targetCount; i++)
        {
            result[i] = baseShare + (i < remainder ? 1 : 0);
        }
        return result;
    }

    /// <summary>
    /// CR 119.4 — coerce an agent-supplied (or null) per-target split into a
    /// legal division of <paramref name="totalDamage"/> across
    /// <paramref name="targetCount"/> targets: exactly
    /// <paramref name="targetCount"/> entries, each ≥ 1, summing to exactly
    /// <paramref name="totalDamage"/>. A wrong-length / null input falls back to
    /// the <see cref="EvenSplit"/>. Each entry is first clamped to ≥ 1, then the
    /// running total is reconciled to the printed total — surplus is shaved off
    /// the LAST targets (never below 1) and any deficit is added to the FIRST
    /// target — so the engine never deals more or fewer than the printed total
    /// even when an agent returns an ill-formed split.
    /// </summary>
    public static IReadOnlyList<int> Normalize(
        IReadOnlyList<int>? proposed, int totalDamage, int targetCount)
    {
        if (targetCount <= 0) return System.Array.Empty<int>();

        // Wrong-length or absent split → even-split fallback (already legal).
        if (proposed == null || proposed.Count != targetCount)
        {
            return EvenSplit(totalDamage, targetCount);
        }

        // Each chosen target must get at least 1 (CR 119.4).
        var result = new int[targetCount];
        for (var i = 0; i < targetCount; i++)
        {
            result[i] = System.Math.Max(1, proposed[i]);
        }

        var total = 0;
        for (var i = 0; i < targetCount; i++) total += result[i];

        // Surplus: shave from the last targets first, never below 1.
        var surplus = total - totalDamage;
        for (var i = targetCount - 1; i >= 0 && surplus > 0; i--)
        {
            var reducible = result[i] - 1;
            var cut = System.Math.Min(reducible, surplus);
            result[i] -= cut;
            surplus -= cut;
        }

        // Deficit: pile onto the first target.
        var deficit = totalDamage - total;
        if (deficit > 0)
        {
            result[0] += deficit;
        }

        return result;
    }
}
