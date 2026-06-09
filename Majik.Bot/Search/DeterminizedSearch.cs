namespace Majik.Bot.Search;

/// <summary>
/// The K-world determinization driver for the MCTS bot.
///
/// <para>
/// Searches K independently-sampled worlds of a determinized root and votes by
/// <em>summed robust child</em>: the root move with the most total root-child
/// visits summed across worlds (tie-broken by summed mean value). Each world
/// re-seeds via <see cref="SimState.WithWorldSeed"/> so its hidden zones are
/// resampled differently, but the searched seat's legal-move set is identical
/// across worlds — so the same <see cref="SimMove.Key"/> recurs and can be
/// summed.
/// </para>
///
/// <para>
/// K is adaptive: a pure function of the two budget ints (see <see cref="KFor"/>).
/// There is no wall-clock branching in <see cref="Run"/> itself — the per-world
/// time budget is conveyed to the <see cref="Mcts"/> via its config by the caller.
/// The caller MUST pass an <see cref="Mcts"/> bounded to <c>perWorldBudgetMs</c>
/// (not the full total): each world runs one <see cref="Mcts.SearchWithStats"/>
/// bounded to per-world, so the K worlds SPLIT the total budget
/// (total ≈ K × perWorldBudgetMs ≈ the configured total, modulo the K clamp) —
/// they do NOT each consume the full total.
/// </para>
/// </summary>
internal static class DeterminizedSearch
{
    /// <summary>
    /// Adaptive world count: <c>clamp(round(totalBudgetMs / perWorldBudgetMs), 1, kMax)</c>.
    /// </summary>
    internal static int KFor(int totalBudgetMs, int perWorldBudgetMs, int kMax) =>
        Math.Clamp((int)Math.Round((double)totalBudgetMs / perWorldBudgetMs), 1, kMax);

    /// <summary>
    /// Per-<see cref="SimMove.Key"/> tally accumulated across worlds: summed visits,
    /// summed total value, and one representative move for that key.
    /// </summary>
    private sealed class KeyTally
    {
        public SimMove Move { get; init; } = null!;
        public int Visits { get; set; }
        public double TotalValue { get; set; }
    }

    /// <summary>
    /// Searches K independently-sampled worlds of <paramref name="determinizedRoot"/>
    /// and returns the summed-robust-child move.
    /// </summary>
    /// <param name="mcts">
    /// The search engine. Its <c>MctsConfig</c> MUST be bounded to
    /// <paramref name="perWorldBudgetMs"/> (not the full total) so the K worlds
    /// split the total budget instead of each running a full-budget search.
    /// </param>
    /// <param name="determinizedRoot">
    /// A determinized root — <see cref="SimState.WorldSeed"/> and
    /// <see cref="SimState.OpponentDecklist"/> must both be set. (The perfect-info
    /// path does not come here.)
    /// </param>
    /// <param name="totalBudgetMs">Total search budget; sets K via <see cref="KFor"/>.</param>
    /// <param name="perWorldBudgetMs">Per-world budget; sets K via <see cref="KFor"/>.</param>
    /// <param name="kMax">Upper clamp on the world count.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="determinizedRoot"/> is not a determinized root.
    /// </exception>
    public static SimMove Run(Mcts mcts, SimState determinizedRoot, int totalBudgetMs,
                              int perWorldBudgetMs = 400, int kMax = 8)
    {
        ArgumentNullException.ThrowIfNull(mcts);
        ArgumentNullException.ThrowIfNull(determinizedRoot);

        if (determinizedRoot.WorldSeed is null || determinizedRoot.OpponentDecklist is null)
            throw new ArgumentException(
                "DeterminizedSearch.Run requires a determinized root (WorldSeed + OpponentDecklist set). " +
                "Use SimState.WithDeterminization before calling; the perfect-info path does not come here.",
                nameof(determinizedRoot));

        var baseSeed = determinizedRoot.WorldSeed.Value;
        var k = KFor(totalBudgetMs, perWorldBudgetMs, kMax);

        var tally = new Dictionary<string, KeyTally>();
        SimMove? firstWorldBest = null;

        for (var w = 0; w < k; w++)
        {
            var world = determinizedRoot.WithWorldSeed(baseSeed + w);
            var res = mcts.SearchWithStats(world);

            firstWorldBest ??= res.Best;

            foreach (var stat in res.RootStats)
            {
                if (!tally.TryGetValue(stat.Move.Key, out var entry))
                {
                    // Keep the first representative SimMove seen for this key.
                    entry = new KeyTally { Move = stat.Move };
                    tally[stat.Move.Key] = entry;
                }
                entry.Visits += stat.Visits;
                entry.TotalValue += stat.TotalValue;
            }
        }

        // ── Forced / zero-visit fallback ───────────────────────────────────────
        // If no real search happened in any world (every world forced → all
        // summed visits are 0), argmax-over-zero is meaningless. Fall back to the
        // first world's Best (well-formed even for a single forced move).
        var maxVisits = tally.Count == 0 ? 0 : tally.Values.Max(t => t.Visits);
        if (maxVisits == 0)
            return firstWorldBest!;

        // ── Summed robust child ────────────────────────────────────────────────
        // Most summed visits; tie-break by summed mean value (guard div-by-zero).
        var winner = tally.Values
            .OrderByDescending(t => t.Visits)
            .ThenByDescending(t => t.Visits > 0 ? t.TotalValue / t.Visits : double.NegativeInfinity)
            .First();

        return winner.Move;
    }
}
