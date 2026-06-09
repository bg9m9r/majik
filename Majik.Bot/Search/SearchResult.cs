namespace Majik.Bot.Search;

/// <summary>
/// Per-root-child statistics from one MCTS search: the move that leads to the
/// child, plus its accumulated visit count and total value. Used by the K-world
/// determinization loop to sum visits across independently-searched worlds and
/// vote. Group across worlds by <see cref="SimMove.Key"/>.
/// </summary>
internal sealed record RootStat(SimMove Move, int Visits, double TotalValue);

/// <summary>
/// The outcome of <see cref="Mcts.SearchWithStats"/>: the chosen robust-child
/// move (<see cref="Best"/>) and the per-root-child statistics
/// (<see cref="RootStats"/>) that backed the choice.
/// </summary>
internal sealed record SearchResult(SimMove Best, IReadOnlyList<RootStat> RootStats);
