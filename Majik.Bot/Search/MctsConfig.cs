namespace Majik.Bot.Search;

/// <summary>
/// Tuning knobs for the UCT MCTS search.
/// </summary>
/// <param name="MaxIterations">Maximum number of select-expand-rollout-backprop iterations.</param>
/// <param name="MaxMillis">Wall-clock time budget in milliseconds (anytime cutoff).</param>
/// <param name="DepthTurns">
/// Rollout depth in FULL turns beyond the current turn number — the playout cap
/// is <c>maxTurns = TurnNumber + DepthTurns</c>, and the engine always plays the
/// remainder of the current (resumed) turn first. Keep small (0–3): deep
/// rollouts wash out because the heuristic strategy recovers most positions to
/// the same win value. Only consulted under
/// <see cref="Search.RolloutDepth.FullTurnPlus"/>; see <see cref="RolloutDepth"/>
/// for how the other depths narrow it (<c>EndOfTurn</c> forces 0,
/// <c>LeafEval</c> skips the playout entirely).
/// </param>
/// <param name="ExplorationC">UCB1 exploration constant (√2 ≈ 1.41 is the standard default).</param>
/// <param name="RolloutDepth">
/// How far the rollout plays out before evaluating (the #2596 rollout-cost
/// lever). Default <see cref="Search.RolloutDepth.FullTurnPlus"/> = today's
/// behaviour, byte-identical.
/// </param>
/// <param name="TreeStateReuse">
/// Tree-state reuse (the snapshot/restore lever): when true, the UCT loop
/// caches each cache-eligible node's state (frozen players + resume context)
/// and expands / rolls out from the NEAREST CACHED ANCESTOR via
/// <see cref="EngineSimulator.AdvanceFrom"/> — replaying only the move
/// suffix instead of the whole root path. Iteration-for-iteration equivalent
/// to the root-replay path (see <c>TreeReuseEquivalenceTests</c>); only the
/// cost changes. Default <b>false</b> = today's behaviour, byte-identical.
/// Requires the simulator to be an <see cref="EngineSimulator"/>; with any
/// other <see cref="ISearchSimulator"/> the flag is inert (root replay).
/// </param>
internal sealed record MctsConfig(
    int MaxIterations = 200,
    int MaxMillis = 2000,
    int DepthTurns = 2,
    double ExplorationC = 1.41,
    RolloutDepth RolloutDepth = RolloutDepth.FullTurnPlus,
    bool TreeStateReuse = false);
