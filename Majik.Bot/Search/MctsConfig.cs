namespace Majik.Bot.Search;

/// <summary>
/// Tuning knobs for the UCT MCTS search.
/// </summary>
/// <param name="MaxIterations">Maximum number of select-expand-rollout-backprop iterations.</param>
/// <param name="MaxMillis">Wall-clock time budget in milliseconds (anytime cutoff).</param>
/// <param name="DepthTurns">
/// Rollout depth in turns beyond the current turn number.
/// Keep small (0–3): deep rollouts wash out because the heuristic strategy
/// recovers most positions to the same win value.
/// </param>
/// <param name="ExplorationC">UCB1 exploration constant (√2 ≈ 1.41 is the standard default).</param>
internal sealed record MctsConfig(
    int MaxIterations = 200,
    int MaxMillis = 2000,
    int DepthTurns = 2,
    double ExplorationC = 1.41);
