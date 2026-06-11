namespace Majik.Bot.Search;

/// <summary>
/// Drives a detached sandbox game to produce decision information (Advance)
/// or a leaf evaluation score (Rollout) for MCTS tree search.
/// </summary>
internal interface ISearchSimulator
{
    /// <summary>
    /// Replay <paramref name="pathFromRoot"/> in a fresh sandbox cloned from
    /// <paramref name="root"/>, then surface the next searched decision.
    ///
    /// <para>
    /// Returns a normal <see cref="SimDecision"/> (IsTerminal=false) with the
    /// legal moves available at the node reached by the path. If the game ends
    /// before another searched decision is reached, returns a terminal marker
    /// (<see cref="SimDecision.IsTerminal"/>=true,
    /// <see cref="SimDecision.TerminalValue"/> set from the leaf evaluation).
    /// </para>
    /// </summary>
    SimDecision Advance(SimState root, IReadOnlyList<SimMove> pathFromRoot);

    /// <summary>
    /// Replay <paramref name="pathFromRoot"/> in a fresh sandbox, then play both
    /// seats to depth or game-over using the heuristic rollout strategy, and
    /// return the leaf <see cref="Majik.Bot.Evaluation.BoardEval"/> score from
    /// the searched seat's perspective.
    ///
    /// <para>
    /// A large positive value indicates a win for the searched seat; a large
    /// negative value indicates a loss. <paramref name="depthTurns"/> bounds the
    /// total turns simulated beyond <see cref="SimState.TurnNumber"/>.
    /// </para>
    /// </summary>
    double Rollout(SimState root, IReadOnlyList<SimMove> pathFromRoot, int depthTurns);
}
