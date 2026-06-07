using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Simulation;
using Majik.Core.Stack;
using Majik.Core.StateMachine;

namespace Majik.Bot.Search;

/// <summary>
/// <see cref="ISearchSimulator"/> implementation that runs real
/// <see cref="SandboxGame"/> instances. Each call to <see cref="Advance"/> or
/// <see cref="Rollout"/> clones the root state into an independent sandbox and
/// drives it until the requested information is available.
///
/// <para>
/// <b>Concurrency contract (from Task A3):</b> <see cref="SandboxGame.ResumeAsync"/>
/// runs synchronously on the caller's thread until the engine first awaits inside
/// the <see cref="SearchAgent"/>. The search side must therefore capture
/// <see cref="SearchAgent.NextDecisionAsync"/> BEFORE calling
/// <see cref="SandboxGame.ResumeAsync"/> and must capture the NEXT decision TCS
/// BEFORE each <see cref="SearchAgent.SupplyMove"/>. A fixed deterministic seed is
/// used so the simulation is reproducible.
/// </para>
///
/// <para>
/// <b>Terminal detection:</b> A game is over when the run task completes. When the
/// run task wins the <c>WhenAny</c> race the game ended before another searched
/// decision was reached and a terminal <see cref="SimDecision"/> is returned.
/// Abandoned run tasks (Advance stops driving after the first decision is captured)
/// have their exceptions observed via a fire-and-forget continuation so the CLR
/// never surfaces an <see cref="UnobservedTaskException"/>.
/// </para>
/// </summary>
public sealed class EngineSimulator : ISearchSimulator
{
    /// <summary>Large reward / penalty applied at terminal nodes (win/loss).</summary>
    private const double WinValue = 1_000.0;
    private const double LossValue = -1_000.0;

    /// <summary>Fixed seed so every Advance/Rollout on the same root is deterministic.</summary>
    private const int FixedSeed = 42;

    private readonly ArchetypeWeights _weights;

    public EngineSimulator(ArchetypeWeights weights)
    {
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
    }

    // ── ISearchSimulator ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public SimDecision Advance(SimState root, IReadOnlyList<SimMove> pathFromRoot)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(pathFromRoot);

        return AdvanceCore(root, pathFromRoot);
    }

    /// <inheritdoc/>
    public double Rollout(SimState root, IReadOnlyList<SimMove> pathFromRoot, int depthTurns)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(pathFromRoot);
        if (depthTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(depthTurns), "depthTurns must be >= 0.");

        return RolloutCore(root, pathFromRoot, depthTurns);
    }

    // ── Implementation ────────────────────────────────────────────────────────

    private SimDecision AdvanceCore(SimState root, IReadOnlyList<SimMove> pathFromRoot)
    {
        var cts = new CancellationTokenSource();

        SearchAgent? searchAgent = null;

        // Build the sandbox. The SearchAgent gets the path as its script so
        // that the first |path| decisions are answered instantly, then capture
        // mode kicks in for the next decision.
        var sandbox = SandboxGame.From(
            root.LivePlayers,
            new GameRandom(FixedSeed),
            p => BuildAgent(p, root, pathFromRoot, rolloutStrategy: null, ref searchAgent));

        var agent = searchAgent
            ?? throw new InvalidOperationException("SearchAgent was not created — searched seat not found in cloned players.");

        // Resolve the cloned active player from the root's active player.
        var clonedActive = sandbox.State.PlayerFor(root.ActivePlayer);

        // CRITICAL: capture the pending decision TCS BEFORE starting the engine.
        // ResumeAsync runs synchronously on this thread until the engine first
        // awaits inside SearchAgent.DecideAsync. At that point the TCS for the
        // first decision is already completed. Capturing here means we will see
        // it as already-done in the WhenAny below.
        var nextDecision = agent.NextDecisionAsync();

        var run = sandbox.ResumeAsync(
            root.Phase,
            clonedActive,
            root.TurnNumber,
            maxTurns: root.TurnNumber + 100, // large cap — Advance stops at first non-pass decision
            ct: cts.Token);

        // Observe the run task's exceptions to avoid UnobservedTaskException
        // (we abandon it after the first non-pass decision is captured).
        ObserveExceptions(run);

        // Drive the engine, draining pass-only Priority decisions automatically
        // (Task B1 scope: Priority only has Pass right now) until we reach a
        // substantive decision (DeclareAttackers / DeclareBlockers) or game over.
        while (true)
        {
            var winner = Task.WhenAny(nextDecision, run).GetAwaiter().GetResult();

            if (ReferenceEquals(winner, run))
            {
                // Game ended before a non-Priority decision was reached.
                cts.Cancel();
                var gameResult = run.GetAwaiter().GetResult();
                var terminalValue = ComputeTerminalValue(gameResult, sandbox.State, root);
                return SimDecision.Terminal(terminalValue);
            }

            var decision = nextDecision.GetAwaiter().GetResult();

            // If this is a pass-only Priority decision, drain it automatically.
            // This handles the BeginningOfCombat and other pass-only priority
            // windows before the substantive decision (DeclareAttackers, etc.).
            if (decision.Kind == SimDecisionKind.Priority
                && decision.LegalMoves.Count == 1
                && decision.LegalMoves[0].IsPass)
            {
                // Capture the NEXT decision TCS BEFORE supplying the move
                // (per the concurrency contract).
                nextDecision = agent.NextDecisionAsync();
                agent.SupplyMove(decision.LegalMoves[0]);
                continue;
            }

            // Non-Priority decision (or Priority with real choices) — this is
            // the MCTS node. Cancel the engine and return.
            cts.Cancel();
            return decision;
        }
    }

    private double RolloutCore(SimState root, IReadOnlyList<SimMove> pathFromRoot, int depthTurns)
    {
        SearchAgent? searchAgent = null;
        var rolloutStrategy = new HeuristicStrategy(new BotConfig(
            ArchetypeName: "Burn")); // Burn weights drive aggressive play in rollout

        // Build the sandbox. The SearchAgent has the path as its script and
        // the heuristic rollout strategy for post-script decisions.
        var sandbox = SandboxGame.From(
            root.LivePlayers,
            new GameRandom(FixedSeed),
            p => BuildAgent(p, root, pathFromRoot, rolloutStrategy, ref searchAgent));

        _ = searchAgent
            ?? throw new InvalidOperationException("SearchAgent was not created — searched seat not found in cloned players.");

        var clonedActive = sandbox.State.PlayerFor(root.ActivePlayer);

        // In rollout mode we AWAIT the run to completion — no decision capture
        // needed because the SearchAgent never pauses (rollout strategy answers
        // everything inline).
        var run = sandbox.ResumeAsync(
            root.Phase,
            clonedActive,
            root.TurnNumber,
            maxTurns: root.TurnNumber + depthTurns,
            ct: CancellationToken.None);

        // Synchronously wait — this is intentional (MCTS rollouts are
        // inherently sequential within a simulation). The run always terminates
        // because the rollout strategy never pauses and maxTurns caps it.
        var gameResult = run.GetAwaiter().GetResult();

        return ComputeTerminalValue(gameResult, sandbox.State, root);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Agent factory called by <see cref="SandboxGame.From"/> for each cloned player.
    /// The searched seat gets a <see cref="SearchAgent"/> (with the path script and
    /// optional rollout strategy); all other seats get a <see cref="DeterministicBotAgent"/>.
    /// </summary>
    private static IPlayerAgent BuildAgent(
        Player clonedPlayer,
        SimState root,
        IReadOnlyList<SimMove> path,
        IBotStrategy? rolloutStrategy,
        ref SearchAgent? captureRef)
    {
        if (clonedPlayer.Id == root.SearchedSeatId)
        {
            var agent = new SearchAgent(
                seat: clonedPlayer,
                script: path,
                rolloutStrategy: rolloutStrategy);
            captureRef = agent;
            return agent;
        }

        return new DeterministicBotAgent();
    }

    /// <summary>
    /// Computes the terminal value for the searched seat based on the game
    /// result. Win = +WinValue; Loss = -LossValue; draw = BoardEval from
    /// the final cloned state.
    /// </summary>
    private double ComputeTerminalValue(
        GameDriver.GameResult gameResult,
        ClonedGame clonedState,
        SimState root)
    {
        // Find the cloned searched seat.
        var clonedSeat = clonedState.Players
            .FirstOrDefault(p => p.Id == root.SearchedSeatId);

        if (clonedSeat == null)
        {
            // Searched seat left the game (should only happen if they lost).
            return LossValue;
        }

        if (gameResult.Winner != null)
        {
            // A winner was determined.
            return gameResult.Winner.Id == root.SearchedSeatId
                ? WinValue
                : LossValue;
        }

        // Draw / max-turns-reached: fall through to BoardEval.
        // Build a minimal GameContext from the final sandbox state so
        // BoardEval can compute the leaf score.
        var allPlayers = clonedState.Players;
        var ctx = BuildLeafContext(clonedSeat, allPlayers);
        return BoardEval.Score(ctx, clonedSeat, _weights);
    }

    /// <summary>
    /// Builds a minimal <see cref="GameContext"/> for leaf evaluation after the
    /// sandbox run has completed. The context does not have a live engine stack
    /// (the run is over), so we provide a fresh empty one.
    /// </summary>
    private static GameContext BuildLeafContext(Player self, IReadOnlyList<Player> allPlayers)
    {
        // A fresh bus + stack is sufficient for BoardEval (it only inspects
        // Player.Zones, Player.LifeTotal, etc. — not the stack itself).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        return new GameContext(
            self: self,
            allPlayers: allPlayers,
            activePlayer: self,  // arbitrary for leaf eval
            turnNumber: 0,
            currentPhase: StepStateType.PreCombatMain,
            stack: stack);
    }

    /// <summary>
    /// Attaches a fire-and-forget continuation that observes (swallows) any
    /// exception from an abandoned run task, preventing
    /// <see cref="UnobservedTaskException"/> from being raised by the CLR
    /// finaliser.
    /// </summary>
    private static void ObserveExceptions(Task task)
    {
        task.ContinueWith(
            static t => _ = t.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
}
