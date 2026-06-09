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
            Accumulate(tally, res.RootStats);
        }

        return Vote(tally, firstWorldBest!);
    }

    /// <summary>
    /// Belief-driven entry: searches one block of worlds per archetype in
    /// <paramref name="allocation"/> (each block stamps that archetype's
    /// <paramref name="allocation"/> decklist plus the shared
    /// <paramref name="observedPublic"/> onto <paramref name="baseRoot"/> per world),
    /// then votes by the same summed-robust-child as <see cref="Run"/>. Unlike
    /// <see cref="Run"/>, <paramref name="baseRoot"/> is the PLAIN capture root (NOT
    /// pre-determinized) — each world attaches its own decklist + seed.
    ///
    /// <para>
    /// World seeds run as a single 0-based counter across the whole allocation, so
    /// a single-archetype allocation of N worlds seeds 0..N-1 — identical to
    /// <see cref="Run"/> on a <see cref="SimState.WithDeterminization(IReadOnlyList{string}, int)"/>
    /// root with <c>worldSeed: 0</c> and K = N.
    /// </para>
    /// </summary>
    /// <param name="mcts">
    /// The search engine; its <c>MctsConfig</c> MUST be bounded to
    /// <paramref name="perWorldBudgetMs"/> (not the full total) — same contract as <see cref="Run"/>.
    /// </param>
    /// <param name="baseRoot">The plain capture root (NOT determinized).</param>
    /// <param name="allocation">
    /// Per-archetype world spread: each entry is a decklist and the number of worlds
    /// to sample against it. The allocation already encodes the total world count
    /// (K = Σ Worlds), so <paramref name="totalBudgetMs"/> / <paramref name="kMax"/>
    /// are accepted for signature symmetry with <see cref="Run"/> but do not size K.
    /// </param>
    /// <param name="observedPublic">
    /// Per-world public observations threaded to the resampler (shared across worlds);
    /// <c>null</c> for the unaugmented path.
    /// </param>
    /// <param name="totalBudgetMs">Accepted for signature symmetry with <see cref="Run"/>; unused (K is encoded by the allocation).</param>
    /// <param name="perWorldBudgetMs">Per-world budget; conveyed to <paramref name="mcts"/> by the caller.</param>
    /// <param name="kMax">Accepted for signature symmetry with <see cref="Run"/>; unused (K is encoded by the allocation).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="allocation"/> is null/empty or sums to zero worlds.</exception>
    public static SimMove RunBelief(Mcts mcts, SimState baseRoot,
        IReadOnlyList<(IReadOnlyList<string> Decklist, int Worlds)> allocation,
        IReadOnlyList<string>? observedPublic,
        int totalBudgetMs, int perWorldBudgetMs = 400, int kMax = 8)
    {
        ArgumentNullException.ThrowIfNull(mcts);
        ArgumentNullException.ThrowIfNull(baseRoot);

        if (allocation is null || allocation.Count == 0)
            throw new ArgumentException("RunBelief needs a non-empty allocation.", nameof(allocation));
        if (allocation.Sum(a => a.Worlds) <= 0)
            throw new ArgumentException("RunBelief needs at least one world to sample.", nameof(allocation));

        var tally = new Dictionary<string, KeyTally>();
        SimMove? firstBest = null;
        var seed = 0;

        foreach (var (decklist, worlds) in allocation)
            for (var w = 0; w < worlds; w++, seed++)
            {
                var world = baseRoot.WithDeterminization(decklist, observedPublic, worldSeed: seed);
                var res = mcts.SearchWithStats(world);
                firstBest ??= res.Best;
                Accumulate(tally, res.RootStats);
            }

        return Vote(tally, firstBest!);
    }

    /// <summary>
    /// Folds one world's per-root-move statistics into the cross-world
    /// <paramref name="tally"/>: keyed by <see cref="SimMove.Key"/>, summing visits
    /// and total value; the first representative <see cref="SimMove"/> seen for a key
    /// is retained.
    /// </summary>
    private static void Accumulate(Dictionary<string, KeyTally> tally, IReadOnlyList<RootStat> rootStats)
    {
        foreach (var stat in rootStats)
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

    /// <summary>
    /// Picks the winning move from a cross-world <paramref name="tally"/> by summed
    /// robust child: most summed visits, tie-broken by summed mean value (guarding
    /// div-by-zero), final tie-break on the move <see cref="SimMove.Key"/> for
    /// Dictionary-order-independent determinism. When no world produced a real
    /// search (every move forced → all summed visits 0), argmax-over-zero is
    /// meaningless, so it falls back to <paramref name="firstBestFallback"/>.
    /// </summary>
    private static SimMove Vote(Dictionary<string, KeyTally> tally, SimMove firstBestFallback)
    {
        // ── Forced / zero-visit fallback ───────────────────────────────────────
        var maxVisits = tally.Count == 0 ? 0 : tally.Values.Max(t => t.Visits);
        if (maxVisits == 0)
            return firstBestFallback;

        // ── Summed robust child ────────────────────────────────────────────────
        var winner = tally.Values
            .OrderByDescending(t => t.Visits)
            .ThenByDescending(t => t.Visits > 0 ? t.TotalValue / t.Visits : double.NegativeInfinity)
            .ThenBy(t => t.Move.Key, StringComparer.Ordinal)
            .First();

        return winner.Move;
    }
}
