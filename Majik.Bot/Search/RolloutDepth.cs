namespace Majik.Bot.Search;

/// <summary>
/// How far <see cref="EngineSimulator"/>'s rollout plays the sandbox out before
/// evaluating — the lever on the #2596 finding that rollout ENGINE DRIVE is
/// 85–90% of MCTS decision cost (~6 ms of ~7 ms/iteration).
///
/// <para>
/// <b>Relationship to <see cref="MctsConfig.DepthTurns"/>:</b> the existing
/// playout machinery is a turn cap — <c>maxTurns = TurnNumber + depthTurns</c>
/// on <see cref="Majik.Core.Game.GameDriver.ResumeGameAsync"/>, which ALWAYS
/// plays the remainder of the current (resumed) turn and then runs full extra
/// turns while <c>turnNumber &lt; maxTurns</c>. This enum NARROWS that loop;
/// it does not add new simulation logic:
/// </para>
/// <list type="bullet">
///   <item><see cref="FullTurnPlus"/> — <c>DepthTurns</c> passes through
///     unchanged (live value 1 = the current turn plus one full turn). Today's
///     behaviour and THE DEFAULT.</item>
///   <item><see cref="EndOfTurn"/> — effective <c>depthTurns = 0</c>: the
///     resumed partial turn still plays to the turn boundary (the immediate
///     crack-back / burn window stays in-sim) but no extra turns follow.
///     ~2× cheaper.</item>
///   <item><see cref="LeafEval"/> — no playout at all: the sandbox is driven
///     only to the decision point (the same drive <c>Advance</c> performs —
///     pass-only priority windows drain so the path's spells resolve) and
///     <see cref="Majik.Bot.Evaluation.BoardEval"/> scores that position.
///     The cheapest variant (~4–8× more iterations per budget), but it starves
///     the terminal-loss signal the risk filter feeds on — probe-gated.</item>
/// </list>
/// </summary>
public enum RolloutDepth
{
    /// <summary>No playout: evaluate at the decision point reached by the path.</summary>
    LeafEval,

    /// <summary>Play out the remainder of the CURRENT turn only (depthTurns=0).</summary>
    EndOfTurn,

    /// <summary>Today's playout: current turn plus <see cref="MctsConfig.DepthTurns"/> full turns. The default.</summary>
    FullTurnPlus,
}
