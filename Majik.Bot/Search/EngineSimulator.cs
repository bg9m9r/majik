using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;
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
///
/// <para>
/// <b>Adversarial opponent (Task D3):</b> The non-searched (opponent) seat is driven
/// by <see cref="BotPlayerAgent"/> using <see cref="Heuristic.HeuristicStrategy"/>,
/// which blocks and attacks sensibly. This makes the search genuinely adversarial:
/// when the searched bot declares attackers the sandbox opponent will declare blockers
/// so MCTS can see trades and correctly penalise bad attacks.
/// The opponent's <see cref="Combat.CombatPolicy"/> combat-search budget is capped at
/// <see cref="OpponentSimCombatBudgetMs"/> (20 ms) so that the blocking decision at
/// each MCTS node expansion does not dominate search time.
/// </para>
/// </summary>
public sealed class EngineSimulator : ISearchSimulator
{
    /// <summary>Large reward / penalty applied at terminal nodes (win/loss).</summary>
    private const double WinValue = 1_000.0;
    private const double LossValue = -1_000.0;

    /// <summary>Fixed seed so every Advance/Rollout on the same root is deterministic.</summary>
    private const int FixedSeed = 42;

    /// <summary>
    /// Combat-search budget (ms) for the sandbox opponent agent (Task D3 perf guard).
    /// The opponent's HeuristicStrategy CombatPolicy stopwatch is capped here so that
    /// blocking at every MCTS node does not blow up search time. 20 ms is enough for
    /// the greedy pass on small boards (which is all that matters for correctness) and
    /// keeps a 200-iteration search well under the 1.5 s MCTS budget.
    /// </summary>
    private const int OpponentSimCombatBudgetMs = 20;

    private readonly ArchetypeWeights _weights;
    private readonly string _archetypeName;

    public EngineSimulator(ArchetypeWeights weights, string archetypeName = "Burn")
    {
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        // archetypeName is used to build the sandbox opponent's BotConfig.
        // The opponent archetype mainly tunes eval weights; any valid archetype works.
        _archetypeName = archetypeName;
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
    public double Rollout(
        SimState root,
        IReadOnlyList<SimMove> pathFromRoot,
        int depthTurns,
        RolloutDepth rolloutDepth = RolloutDepth.FullTurnPlus)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(pathFromRoot);
        if (depthTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(depthTurns), "depthTurns must be >= 0.");

        return RolloutCore(root, pathFromRoot, depthTurns, rolloutDepth);
    }

    // ── Implementation ────────────────────────────────────────────────────────

    /// <summary>
    /// Executes <paramref name="work"/> with <see cref="SynchronizationContext.Current"/>
    /// set to <c>null</c>, then restores the original context on exit.
    ///
    /// <para>
    /// <b>Why this is needed:</b> the engine is fully async internally
    /// (TurnDriver, PriorityLoop, CombatFlow all use <c>async Task</c>).  When
    /// <see cref="AdvanceCore"/> or <see cref="RolloutCore"/> is called from an
    /// xUnit test worker (which installs <c>MaxConcurrencySyncContext</c>) the
    /// state-machine continuations of those async methods capture xUnit's context
    /// at their first suspension.  On completion — driven inline by
    /// <see cref="SearchAgent.SupplyMove"/> — the continuation is posted to
    /// xUnit's context queue.  If all xUnit worker threads are already blocked
    /// (waiting at <c>GetResult()</c>) the posted item never runs → deadlock.
    ///
    /// Clearing the context before <see cref="SandboxGame.ResumeAsync"/> ensures
    /// that every <c>await</c> in the engine chain captures <c>null</c>.  With a
    /// null context and a thread-pool thread as completing thread,
    /// <c>AwaitTaskContinuation.IsValidLocationForInlining()</c> returns <c>true</c>
    /// and continuations execute <i>inline</i> on the search thread — no thread-pool
    /// slot needed, no scheduler post, no starvation on 1–2 cores.
    /// </para>
    ///
    /// <para>
    /// The context is restored in a <c>finally</c> block so the caller's thread
    /// (e.g. the xUnit worker) gets its original context back after the search
    /// step completes.
    /// </para>
    /// </summary>
    private static T WithNullSyncContext<T>(Func<T> work)
    {
        var prev = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try   { return work(); }
        finally { SynchronizationContext.SetSynchronizationContext(prev); }
    }

    private SimDecision AdvanceCore(SimState root, IReadOnlyList<SimMove> pathFromRoot)
        => WithNullSyncContext(() => DriveToDecisionUnsafe(root, pathFromRoot).Decision);

    /// <summary>
    /// Tree-state-reuse SPIKE seam (additive, internal): the same drive as
    /// <see cref="Advance"/> but ALSO returns the paused sandbox so a caller can
    /// snapshot the reached position (clone the paused players), and optionally
    /// observes the sandbox right after construction — BEFORE the engine starts —
    /// so bus subscriptions (turn/phase tracking) see every event of the drive.
    /// </summary>
    internal (SimDecision Decision, SandboxGame Sandbox) AdvanceWithSandbox(
        SimState root,
        IReadOnlyList<SimMove> pathFromRoot,
        Action<SandboxGame>? onSandboxBuilt = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(pathFromRoot);
        return WithNullSyncContext(() => DriveToDecisionUnsafe(root, pathFromRoot, onSandboxBuilt));
    }

    // ── Tree-state reuse (Task 2): AdvanceFrom + node snapshots ──────────────

    /// <summary>
    /// Advance from a CACHED tree-node state instead of the search root: clone
    /// <paramref name="cachedPlayers"/> (the frozen players of a
    /// <see cref="NodeSnapshot"/>, or the root clone source itself — the root
    /// is always a valid cache), rebuild a sandbox at
    /// <paramref name="ctx"/>'s turn/phase with the recorded per-seat land
    /// drops seeded, replay ONLY <paramref name="suffix"/>, and stop at the
    /// next substantive decision — exactly the position a full
    /// <c>Advance(root, fullPath)</c> would reach, at the cost of the
    /// inter-node gap instead of the whole path (spike-proven ≈3× at depth 6).
    ///
    /// <para>Returns the reached decision plus the NEW position's snapshot —
    /// or a null snapshot when the position is terminal or cache-ineligible
    /// (<see cref="SnapshotPolicy.IsCacheEligible"/>: non-empty stack /
    /// mid-combat — spike BREAKs 1 and 3). Ineligible positions simply don't
    /// cache; Task 3's descent falls back to the nearest cached ancestor.</para>
    /// </summary>
    /// <param name="cachedPlayers">Frozen players at the cached position (NOT mutated — cloned internally).</param>
    /// <param name="ctx">Resume context recorded when the cache was captured.</param>
    /// <param name="suffix">Moves to replay from the cached position (often a single move).</param>
    /// <param name="searchedSeatId">The searched seat (stable <see cref="Player.Id"/> across clones).</param>
    internal (SimDecision Decision, NodeSnapshot? Snapshot) AdvanceFrom(
        IReadOnlyList<Player> cachedPlayers,
        ResumeCtx ctx,
        IReadOnlyList<SimMove> suffix,
        Guid searchedSeatId)
    {
        ArgumentNullException.ThrowIfNull(cachedPlayers);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(suffix);

        var state = StateFromCache(cachedPlayers, ctx, searchedSeatId);

        return WithNullSyncContext(() => DriveWithSnapshotUnsafe(state, suffix, ctx.LandDropsUsed));
    }

    /// <summary>
    /// Rollout from a CACHED tree-node state instead of the search root (the
    /// reuse counterpart of <see cref="Rollout"/>): clone
    /// <paramref name="cachedPlayers"/>, rebuild a sandbox at
    /// <paramref name="ctx"/>'s turn/phase with the recorded per-seat land
    /// drops seeded (spike BREAK 2 — without it the playout re-offers a
    /// consumed land drop), replay ONLY <paramref name="suffix"/>, then play
    /// out per <paramref name="rolloutDepth"/> and return the leaf score.
    /// Launching the playout from the leaf's own cache (empty suffix) skips
    /// the whole root-path replay every iteration.
    /// </summary>
    /// <param name="cachedPlayers">Frozen players at the cached position (NOT mutated — cloned internally).</param>
    /// <param name="ctx">Resume context recorded when the cache was captured.</param>
    /// <param name="suffix">Moves to replay from the cached position (empty when the cache IS the leaf).</param>
    /// <param name="searchedSeatId">The searched seat (stable <see cref="Player.Id"/> across clones).</param>
    /// <param name="depthTurns">Playout cap in full turns beyond the SEARCH ROOT's turn (as <see cref="Rollout"/>).</param>
    /// <param name="anchorTurnNumber">
    /// The SEARCH ROOT's turn number — the playout horizon is the ABSOLUTE
    /// turn cap <c>anchorTurnNumber + depthTurns</c>, exactly the cap a
    /// root-replay <see cref="Rollout"/> computes. Anchoring at the cache's
    /// own (possibly later) turn instead would silently grant cross-turn
    /// leaves a LONGER playout than the root path gives them — a divergence
    /// the equivalence gate caught.
    /// </param>
    /// <param name="rolloutDepth">Playout truncation (as <see cref="Rollout"/>).</param>
    internal double RolloutFrom(
        IReadOnlyList<Player> cachedPlayers,
        ResumeCtx ctx,
        IReadOnlyList<SimMove> suffix,
        Guid searchedSeatId,
        int depthTurns,
        int anchorTurnNumber,
        RolloutDepth rolloutDepth = RolloutDepth.FullTurnPlus)
    {
        ArgumentNullException.ThrowIfNull(cachedPlayers);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(suffix);
        if (depthTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(depthTurns), "depthTurns must be >= 0.");

        var state = StateFromCache(cachedPlayers, ctx, searchedSeatId);

        return WithNullSyncContext(
            () => RolloutCoreUnsafe(
                state, suffix, depthTurns, rolloutDepth, ctx.LandDropsUsed, anchorTurnNumber));
    }

    /// <summary>
    /// Wraps a cached node state in a SimState so the shared drives can clone
    /// + resume + replay from it. The cached players ARE the position: a
    /// perfect-info SimState (snapshots of determinized worlds are already
    /// materialized, so no WorldSeed/decklist is attached).
    /// </summary>
    private static SimState StateFromCache(
        IReadOnlyList<Player> cachedPlayers, ResumeCtx ctx, Guid searchedSeatId)
    {
        var active = cachedPlayers.FirstOrDefault(p => p.Id == ctx.ActivePlayerId)
            ?? throw new InvalidOperationException("Active player not found in cached players.");
        var searched = cachedPlayers.FirstOrDefault(p => p.Id == searchedSeatId)
            ?? throw new InvalidOperationException("Searched seat not found in cached players.");

        return SimState.Capture(cachedPlayers, active, ctx.TurnNumber, ctx.Phase, searched);
    }

    /// <summary>
    /// The snapshot-capturing drive: <see cref="DriveToDecisionUnsafe"/> with
    /// the spike's observer seam tracking turn / phase / active seat via the
    /// sandbox bus, followed by capture of the reached position when it is
    /// cache-eligible. Capture = one <see cref="GameStateCloner.Clone"/> of the
    /// paused players (~0.12 ms) + the per-seat land-drop tally read off the
    /// sandbox's tracker (spike BREAK 2).
    /// </summary>
    private (SimDecision Decision, NodeSnapshot? Snapshot) DriveWithSnapshotUnsafe(
        SimState state,
        IReadOnlyList<SimMove> path,
        IReadOnlyDictionary<Guid, int>? landDropsUsed)
    {
        var turnNumber = state.TurnNumber;
        var phase = state.Phase;
        var activePlayerId = state.ActivePlayer.Id;

        var (decision, sandbox) = DriveToDecisionUnsafe(
            state,
            path,
            onSandboxBuilt: sb =>
            {
                sb.Bus.Subscribe<TurnStartedEvent>(e =>
                {
                    turnNumber = e.TurnNumber;
                    activePlayerId = e.Player.Id;
                });
                sb.Bus.Subscribe<PhaseStateChangedEvent>(e => phase = e.CurrentState);
            },
            landDropsUsed: landDropsUsed);

        if (decision.IsTerminal
            || !SnapshotPolicy.IsCacheEligible(sandbox.State.Players, decision, phase))
        {
            return (decision, null);
        }

        // Freeze the paused position: one defensive clone so later engine
        // activity (or the caller) can never mutate the cache.
        var frozen = GameStateCloner.Clone(sandbox.State.Players).Players;

        var resumeCtx = new ResumeCtx(
            turnNumber, phase, activePlayerId,
            SuffixFromParent: path,
            LandDropsUsed: CaptureLandDrops(sandbox));

        return (decision, new NodeSnapshot(frozen, resumeCtx, IsCacheEligible: true));
    }

    /// <summary>
    /// Per-seat land drops consumed in the sandbox's CURRENT turn, keyed by
    /// stable <see cref="Player.Id"/> (spike BREAK 2 — the tally lives on the
    /// driver, not the players, so the snapshot must carry it explicitly).
    /// </summary>
    private static IReadOnlyDictionary<Guid, int> CaptureLandDrops(SandboxGame sandbox)
    {
        Dictionary<Guid, int>? drops = null;
        foreach (var p in sandbox.State.Players)
        {
            var used = sandbox.LandDrops.DropsUsedThisTurn(p);
            if (used > 0)
                (drops ??= new Dictionary<Guid, int>())[p.Id] = used;
        }
        return drops ?? EmptyLandDrops;
    }

    private static readonly IReadOnlyDictionary<Guid, int> EmptyLandDrops =
        new Dictionary<Guid, int>();

    /// <summary>
    /// The shared Advance/LeafEval drive: replay the path in a fresh sandbox and
    /// stop at the next substantive decision (or game over). Returns the decision
    /// (terminal marker when the game ended first) TOGETHER with the sandbox so
    /// <see cref="RolloutDepth.LeafEval"/> can evaluate the position at that
    /// exact point.
    ///
    /// <para><paramref name="landDropsUsed"/> (tree-state reuse, spike BREAK 2):
    /// per-seat land drops already consumed in the resumed turn, seeded into the
    /// sandbox's fresh <c>LandDropTracker</c>. Null (default, every pre-existing
    /// caller) = fresh tally, byte-identical to before.</para>
    /// </summary>
    private (SimDecision Decision, SandboxGame Sandbox) DriveToDecisionUnsafe(
        SimState root,
        IReadOnlyList<SimMove> pathFromRoot,
        Action<SandboxGame>? onSandboxBuilt = null,
        IReadOnlyDictionary<Guid, int>? landDropsUsed = null)
    {
        var cts = new CancellationTokenSource();

        SearchAgent? searchAgent = null;

        // Build the sandbox. The SearchAgent gets the path as its script so
        // that the first |path| decisions are answered instantly, then capture
        // mode kicks in for the next decision. Determinized roots clone from the
        // per-world MATERIALIZED base (sampled zones already carry prod-built
        // cards); perfect-info roots clone the live players, byte-identical to
        // before.
        var sandbox = SandboxGame.From(
            ResolveCloneSource(root),
            new GameRandom(FixedSeed),
            p => BuildAgent(p, root, pathFromRoot, rolloutStrategy: null, ref searchAgent),
            cardRepo: SharedCardData.Repo,
            landDropsUsed: landDropsUsed);

        var agent = searchAgent
            ?? throw new InvalidOperationException("SearchAgent was not created — searched seat not found in cloned players.");

        // Spike seam: let the caller observe (subscribe to) the sandbox BEFORE
        // the engine starts so no event of this drive is missed.
        onSandboxBuilt?.Invoke(sandbox);

        // Resolve the cloned active player from the root's active player by Id
        // (Player.Id survives cloning; the clone SOURCE may be the world base,
        // whose players are not reference-keys for root.ActivePlayer).
        var clonedActive = ClonedActivePlayer(sandbox, root);

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
                return (SimDecision.Terminal(terminalValue), sandbox);
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
            return (decision, sandbox);
        }
    }

    private double RolloutCore(
        SimState root, IReadOnlyList<SimMove> pathFromRoot, int depthTurns, RolloutDepth rolloutDepth)
        => WithNullSyncContext(() => RolloutCoreUnsafe(root, pathFromRoot, depthTurns, rolloutDepth));

    /// <summary>
    /// <paramref name="landDropsUsed"/> (tree-state reuse, spike BREAK 2):
    /// per-seat land drops already consumed in the resumed turn, seeded into
    /// the playout sandbox's fresh <c>LandDropTracker</c> — launching a
    /// rollout from a node cache without it would re-offer a consumed drop to
    /// the playout policy. <paramref name="anchorTurnNumber"/> (tree-state
    /// reuse): the turn the playout horizon is anchored at — see
    /// <see cref="RolloutFrom"/>; may sit BELOW <paramref name="root"/>'s own
    /// turn for a cross-turn cache, in which case the driver plays the
    /// resumed partial turn and stops (the same truncation the root-replay
    /// path applies when its turn cap lands mid-path). Null defaults
    /// (every pre-existing caller) = fresh tally / root-anchored horizon,
    /// byte-identical to before.
    /// </summary>
    private double RolloutCoreUnsafe(
        SimState root, IReadOnlyList<SimMove> pathFromRoot, int depthTurns, RolloutDepth rolloutDepth,
        IReadOnlyDictionary<Guid, int>? landDropsUsed = null,
        int? anchorTurnNumber = null)
    {
        // LeafEval: NO playout. Drive to the decision point the path leads to
        // (the same drive Advance performs — pass-only priority windows drain,
        // so the path's spells RESOLVE before evaluation) and score that
        // position. The expensive both-seats heuristic playout is skipped.
        if (rolloutDepth == RolloutDepth.LeafEval)
            return LeafEvalUnsafe(root, pathFromRoot, landDropsUsed);

        // EndOfTurn narrows the existing turn-cap machinery to the current-turn
        // boundary (maxTurns = TurnNumber + 0: the resumed partial turn always
        // plays out; zero full extra turns follow). FullTurnPlus = unchanged.
        var effectiveDepthTurns = rolloutDepth == RolloutDepth.EndOfTurn ? 0 : depthTurns;

        SearchAgent? searchAgent = null;
        var rolloutStrategy = new HeuristicStrategy(new BotConfig(
            ArchetypeName: "Burn")); // Burn weights drive aggressive play in rollout

        // Build the sandbox. The SearchAgent has the path as its script and
        // the heuristic rollout strategy for post-script decisions. Clone
        // source: per-world materialized base for determinized roots (see
        // AdvanceCoreUnsafe), live players for perfect-info roots.
        var sandbox = SandboxGame.From(
            ResolveCloneSource(root),
            new GameRandom(FixedSeed),
            p => BuildAgent(p, root, pathFromRoot, rolloutStrategy, ref searchAgent),
            cardRepo: SharedCardData.Repo,
            landDropsUsed: landDropsUsed);

        _ = searchAgent
            ?? throw new InvalidOperationException("SearchAgent was not created — searched seat not found in cloned players.");

        var clonedActive = ClonedActivePlayer(sandbox, root);

        // In rollout mode we AWAIT the run to completion — no decision capture
        // needed because the SearchAgent never pauses (rollout strategy answers
        // everything inline).
        var run = sandbox.ResumeAsync(
            root.Phase,
            clonedActive,
            root.TurnNumber,
            maxTurns: (anchorTurnNumber ?? root.TurnNumber) + effectiveDepthTurns,
            ct: CancellationToken.None);

        // Synchronously wait — this is intentional (MCTS rollouts are
        // inherently sequential within a simulation). The run always terminates
        // because the rollout strategy never pauses and maxTurns caps it.
        var gameResult = run.GetAwaiter().GetResult();

        return ComputeTerminalValue(gameResult, sandbox.State, root);
    }

    /// <summary>
    /// <see cref="RolloutDepth.LeafEval"/> rollout: drive to the decision point
    /// (shared <see cref="DriveToDecisionUnsafe"/> machinery) and return
    /// <see cref="BoardEval.Score"/> there — no playout. If the game ended
    /// before a decision was reached, the terminal value is returned instead
    /// (same scale as the playout's <see cref="ComputeTerminalValue"/>).
    /// <paramref name="landDropsUsed"/>: see <see cref="RolloutCoreUnsafe"/>.
    /// </summary>
    private double LeafEvalUnsafe(
        SimState root, IReadOnlyList<SimMove> pathFromRoot,
        IReadOnlyDictionary<Guid, int>? landDropsUsed = null)
    {
        var (decision, sandbox) = DriveToDecisionUnsafe(root, pathFromRoot, landDropsUsed: landDropsUsed);

        if (decision.IsTerminal)
            return decision.TerminalValue;

        var clonedSeat = sandbox.State.Players
            .FirstOrDefault(p => p.Id == root.SearchedSeatId);
        if (clonedSeat == null)
        {
            // Searched seat left the game (should only happen if they lost).
            return LossValue;
        }

        var ctx = BuildLeafContext(clonedSeat, sandbox.State.Players);
        return BoardEval.Score(ctx, clonedSeat, _weights);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Agent factory called by <see cref="SandboxGame.From"/> for each cloned player.
    ///
    /// <para>
    /// The searched seat gets a <see cref="SearchAgent"/> (with the path script and
    /// optional rollout strategy). The opponent seat gets a <see cref="BotPlayerAgent"/>
    /// backed by <see cref="Heuristic.HeuristicStrategy"/> — it blocks and attacks
    /// sensibly, making the search adversarial. The opponent's
    /// <see cref="Combat.CombatPolicy"/> budget is capped at
    /// <see cref="OpponentSimCombatBudgetMs"/> so that its blocking call at each MCTS
    /// node does not dominate search time (Task D3 perf guard).
    /// </para>
    /// </summary>
    private IPlayerAgent BuildAgent(
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

        // Adversarial opponent: HeuristicStrategy with a capped combat budget.
        // This makes the sandbox genuinely adversarial — the opponent blocks when
        // profitable, so MCTS can observe bad-trade outcomes and penalise them.
        // SimCombatBudgetMs caps the CombatPolicy stopwatch to keep each node
        // expansion fast (see OpponentSimCombatBudgetMs).
        var opponentConfig = new BotConfig(
            ArchetypeName: _archetypeName,
            Strategy: "heuristic",
            SimCombatBudgetMs: OpponentSimCombatBudgetMs);
        return new BotPlayerAgent(clonedPlayer, opponentConfig);
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

    /// <summary>
    /// The clone source every sandbox is built from. Perfect-info roots (no
    /// WorldSeed / decklist) clone the LIVE players — byte-identical to before.
    /// Determinized roots clone the per-world MATERIALIZED base: the live players
    /// cloned ONCE, hidden zones resampled ONCE with REAL prod-built cards
    /// (<see cref="DeckCardBuilder"/>), cached on
    /// <see cref="SimState.MaterializedWorldPlayers"/>. Every per-sim clone of the
    /// base preserves the sampled zones, so the old per-clone shell resample is
    /// gone — same determinism (one seeded resample instead of K identical ones),
    /// far better fidelity (sampled cards are castable prod cards).
    ///
    /// <para><b>Stack / turn-state mirroring:</b> the world base is built via the
    /// players-only <see cref="GameStateCloner.Clone(IReadOnlyList{Player})"/>
    /// overload — deliberately mirroring the per-sim path, which also passes
    /// null stack / null turn-state to <see cref="SandboxGame.From"/>. Nothing
    /// to mirror until the sim path itself starts carrying them.</para>
    ///
    /// <para><b>Tree-state reuse:</b> internal (not private) because this list
    /// IS the search root's cache — <see cref="AdvanceFrom"/> on it with
    /// <see cref="ResumeCtx.ForRoot"/> is the root-level entry of the reuse
    /// chain (Task 3 descent + the AdvanceFrom equivalence tests).</para>
    /// </summary>
    internal static IReadOnlyList<Player> ResolveCloneSource(SimState root)
    {
        if (root.WorldSeed is not int seed || root.OpponentDecklist is not { } deck)
            return root.LivePlayers;                                   // perfect-info: unchanged
        if (root.MaterializedWorldPlayers is { } cached) return cached;

        var worldBase = GameStateCloner.Clone(root.LivePlayers);
        DeterminizationSampler.Resample(worldBase.Players, root.SearchedSeatId, deck, seed,
            observedPublic: root.ObservedPublic,
            buildCard: BuildSampledCard);
        root.MaterializedWorldPlayers = worldBase.Players;
        return worldBase.Players;
    }

    /// <summary>
    /// Builds one sampled opponent card EXACTLY like a live-deck card: the prod
    /// <see cref="DeckCardBuilder"/> path (repo shell + named-factory routing +
    /// binder chain). A scratch ReplacementBus / ContinuousEffectsService per call
    /// is fine — materialization happens once per world, and per-call scratch
    /// services rule out any cross-world state bleed. Triggers / zones / eventBus
    /// are null with full surface parity (the sandbox wires its own live services
    /// when the card is later cloned into a sim and cast).
    /// </summary>
    private static ICard BuildSampledCard(string name, Player owner) =>
        DeckCardBuilder.Build(name, owner, SharedCardData.Repo,
            new ReplacementBus(), new ContinuousEffectsService(),
            triggers: null, zones: null, eventBus: null, routeThroughNamedFactories: true);

    /// <summary>
    /// Resolves the cloned active player by <see cref="Player.Id"/> (stable across
    /// clones). Reference-keyed <see cref="ClonedGame.PlayerFor"/> cannot be used:
    /// for determinized roots the sandbox is cloned from the world BASE, so
    /// <c>root.ActivePlayer</c> (a live player) is not a key in its PlayerMap.
    /// </summary>
    private static Player ClonedActivePlayer(SandboxGame sandbox, SimState root)
        => sandbox.State.Players.FirstOrDefault(p => p.Id == root.ActivePlayer.Id)
            ?? throw new InvalidOperationException(
                "Active player not found in cloned players.");

    /// <summary>
    /// Test-only observation hook (no game is driven): performs ONE clone via the
    /// same <see cref="SandboxGame.From"/> + <see cref="ResolveCloneSource"/> path
    /// used by the search, then returns the cloned opponent's hand card names.
    /// For a determinized root this materializes (and caches) the world base, so
    /// the returned hand is the sampled hand from a clone of that base. With a
    /// null world seed the opponent's ACTUAL hand is returned (proving the
    /// perfect-info path is untouched). Lets the determinization wiring be
    /// verified without running a whole game.
    /// </summary>
    internal IReadOnlyList<string> DebugSampledOpponentHand(SimState root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // The sandbox is never run here, so the agent choice is irrelevant; a
        // bare SearchAgent (empty script) satisfies SandboxGame.From's factory.
        var sandbox = SandboxGame.From(
            ResolveCloneSource(root),
            new GameRandom(FixedSeed),
            p => new SearchAgent(p),
            cardRepo: SharedCardData.Repo);

        var opp = sandbox.State.Players.FirstOrDefault(p => p.Id != root.SearchedSeatId)
            ?? throw new InvalidOperationException("No opponent seat in cloned players.");

        return opp.Zones.Hand.GetCards().Select(c => c.Name).ToList();
    }
}
