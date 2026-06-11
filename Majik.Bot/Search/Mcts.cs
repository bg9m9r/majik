using System.Diagnostics;
using Majik.Core.Players;

namespace Majik.Bot.Search;

/// <summary>
/// UCT (Upper Confidence Bounds applied to Trees) Monte Carlo Tree Search.
///
/// <para>
/// Searches only the bot's decisions — no opponent min-node modelling (Phase 1).
/// All values are from the searched seat's POV (higher = better); backpropagation
/// is straight accumulation, not negamax.
/// </para>
///
/// <para>
/// Returns the <em>robust child</em>: the root child with the most visits
/// (most-visited = most evidence). Ties broken by higher mean value.
/// </para>
/// </summary>
internal sealed class Mcts
{
    private readonly ISearchSimulator _sim;
    private readonly MctsConfig _config;

    /// <summary>
    /// Non-null iff <see cref="MctsConfig.TreeStateReuse"/> is enabled AND the
    /// simulator is the real <see cref="EngineSimulator"/> (the only sim with
    /// snapshot/restore — <c>AdvanceFrom</c>/<c>RolloutFrom</c>). With any
    /// other <see cref="ISearchSimulator"/> the flag is inert and the loop
    /// stays on the root-replay path.
    /// </summary>
    private readonly EngineSimulator? _reuseSim;

    public Mcts(ISearchSimulator sim, MctsConfig config)
    {
        _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _reuseSim = config.TreeStateReuse ? sim as EngineSimulator : null;
    }

    // ── Instrumentation (equivalence gate + diagnostics) ─────────────────────

    /// <summary>
    /// Iteration-trace hook (test instrumentation, null in production): called
    /// once per UCT iteration with the evaluated node's path key
    /// (<see cref="SimMove.Key"/>s joined root→node) and the value that was
    /// backpropagated. The equivalence gate records this sequence for reuse
    /// OFF vs ON and asserts identity.
    /// </summary>
    internal Action<string, double>? OnIterationTrace { get; set; }

    /// <summary>The bounds this search runs under (test instrumentation — lets
    /// config-threading tests pin the per-world determinized split without
    /// running a search).</summary>
    internal MctsConfig Config => _config;

    /// <summary>How many expansions went through the reuse path
    /// (<see cref="EngineSimulator.AdvanceFrom"/>) — 0 unless reuse is active.</summary>
    internal int ReuseExpansions { get; private set; }

    /// <summary>How many rollouts launched from a cached node state
    /// (<see cref="EngineSimulator.RolloutFrom"/>) — 0 unless reuse is active.</summary>
    internal int ReuseRollouts { get; private set; }

    /// <summary>
    /// Run UCT from the given root state and return the best (robust-child)
    /// root move. Thin wrapper over <see cref="SearchWithStats"/>; behaviour is
    /// identical (same UCT loop, same robust-child selection).
    /// </summary>
    public SimMove Search(SimState root) => SearchWithStats(root).Best;

    /// <summary>
    /// Run UCT from the given root state and return both the best (robust-child)
    /// root move and the per-root-child statistics that backed the choice.
    ///
    /// <para>
    /// The K-world determinization loop calls this once per world and sums the
    /// returned <see cref="RootStat.Visits"/> across worlds (grouped by
    /// <see cref="SimMove.Key"/>) to vote on a single move.
    /// </para>
    ///
    /// <para>
    /// On a forced/trivial root (a single legal move) the search short-circuits
    /// before building a tree: <see cref="SearchResult.Best"/> is that move and
    /// <see cref="SearchResult.RootStats"/> is a single un-searched entry
    /// (Visits = 0, TotalValue = 0) for it.
    /// </para>
    /// </summary>
    public SearchResult SearchWithStats(SimState root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // ── Short-circuit ──────────────────────────────────────────────────────
        var rootDecision = _sim.Advance(root, Array.Empty<SimMove>());

        if (rootDecision.IsTerminal)
            throw new InvalidOperationException("Root position is already terminal — no move to return.");

        if (rootDecision.LegalMoves.Count == 1)
        {
            // Forced move: no tree is built. Surface it as Best with a single
            // un-searched RootStat so callers always see a well-formed result.
            var only = rootDecision.LegalMoves[0];
            return new SearchResult(only, new[] { new RootStat(only, Visits: 0, TotalValue: 0.0) });
        }

        // ── Build root node ────────────────────────────────────────────────────
        var rootNode = new MctsNode(incomingMove: null, rootDecision.LegalMoves);

        // Tree-state reuse: the root is ALWAYS cached — its state IS the
        // search's clone source (for determinized roots this materializes the
        // world base once, exactly as the first Advance would), paired with
        // the root's own resume context. Every descent therefore always finds
        // a cached ancestor.
        if (_reuseSim is not null)
        {
            rootNode.CachedPlayers = EngineSimulator.ResolveCloneSource(root);
            rootNode.ResumeContext = ResumeCtx.ForRoot(root);
        }

        var sw = Stopwatch.StartNew();
        int iterations = 0;

        // ── Main loop ──────────────────────────────────────────────────────────
        while (iterations < _config.MaxIterations && sw.ElapsedMilliseconds < _config.MaxMillis)
        {
            // ── Select ────────────────────────────────────────────────────────
            // Descend from root following UCB1 while the node is fully expanded
            // and has children, accumulating the path of moves.
            var node = rootNode;
            var pathMoves = new List<SimMove>(); // moves from root → current node
            var nodePath = new List<MctsNode>(); // nodes from root → current (for backprop)
            nodePath.Add(rootNode);

            while (node.IsFullyExpanded && !node.IsLeaf)
            {
                node = node.SelectChildUcb1(_config.ExplorationC);
                pathMoves.Add(node.IncomingMove!);
                nodePath.Add(node);
            }

            // ── Expand ────────────────────────────────────────────────────────
            // If the node has untried moves, expand one.
            MctsNode evaluatedNode;
            IReadOnlyList<SimMove> evaluatedPath;
            bool isTerminalExpansion = false;
            double terminalValue = 0.0;

            if (!node.IsFullyExpanded)
            {
                // Dequeue the next untried move.
                var move = node.UntriedMoves.Dequeue();

                // Build the path to this new child.
                var childPath = new List<SimMove>(pathMoves) { move };

                // Advance the simulator to get the child's legal moves.
                // Reuse path: expand from the NEAREST CACHED ANCESTOR (the
                // parent when it is cached; the root at the latest) replaying
                // only the move suffix, and capture the child's snapshot.
                SimDecision childDecision;
                NodeSnapshot? childSnapshot = null;
                if (_reuseSim is { } reuseSim)
                {
                    var (cache, ctx, movesFromRoot) = NearestCachedAncestor(nodePath);
                    var suffix = SuffixFrom(pathMoves, movesFromRoot, move);
                    (childDecision, childSnapshot) =
                        reuseSim.AdvanceFrom(cache, ctx, suffix, root.SearchedSeatId);
                    ReuseExpansions++;
                }
                else
                {
                    childDecision = _sim.Advance(root, childPath);
                }

                if (childDecision.IsTerminal)
                {
                    // Child is a terminal — add a node with no legal moves and
                    // use the terminal value directly (no rollout needed).
                    evaluatedNode = node.AddChild(move, Array.Empty<SimMove>());
                    isTerminalExpansion = true;
                    terminalValue = childDecision.TerminalValue;
                }
                else
                {
                    evaluatedNode = node.AddChild(move, childDecision.LegalMoves);
                }

                // Cache the child's state when AdvanceFrom captured one
                // (cache-eligible positions only — see SnapshotPolicy;
                // ineligible/terminal positions stay uncached and later
                // descents fall back to this node's nearest cached ancestor).
                if (childSnapshot is not null)
                {
                    evaluatedNode.CachedPlayers = childSnapshot.Players;
                    evaluatedNode.ResumeContext = childSnapshot.Ctx;
                }

                nodePath.Add(evaluatedNode);
                evaluatedPath = childPath;
            }
            else
            {
                // Node has no untried moves AND no children (leaf, terminal).
                // Evaluate in-place.
                evaluatedNode = node;
                evaluatedPath = pathMoves;
            }

            // ── Rollout ───────────────────────────────────────────────────────
            double value;
            if (isTerminalExpansion)
            {
                value = terminalValue;
            }
            else if (_reuseSim is { } rolloutSim)
            {
                // Reuse path: launch the playout from the evaluated node's own
                // cache when it has one (empty suffix — the cache IS the leaf),
                // else from the nearest cached ancestor with the accumulated
                // suffix. nodePath and evaluatedPath are aligned here:
                // nodePath[i] is reached by the first i moves of evaluatedPath.
                //
                // Horizon guard: the playout's turn cap is ABSOLUTE
                // (root turn + depth — see RolloutFrom's anchor), and the
                // root-replay path TRUNCATES mid-path when a node sits beyond
                // it. A cache in a turn past the horizon can therefore not
                // reproduce that playout — restrict the walk to ancestors at
                // or below the horizon (the root always qualifies). LeafEval
                // runs no playout, so any cache works.
                var horizonTurn = _config.RolloutDepth switch
                {
                    RolloutDepth.LeafEval => int.MaxValue,
                    RolloutDepth.EndOfTurn => root.TurnNumber,
                    _ => root.TurnNumber + _config.DepthTurns,
                };
                var (cache, ctx, movesFromRoot) = NearestCachedAncestor(nodePath, horizonTurn);
                var suffix = SuffixFrom(evaluatedPath, movesFromRoot, extraMove: null);
                value = rolloutSim.RolloutFrom(
                    cache, ctx, suffix, root.SearchedSeatId,
                    _config.DepthTurns, anchorTurnNumber: root.TurnNumber, _config.RolloutDepth);
                ReuseRollouts++;
            }
            else
            {
                value = _sim.Rollout(root, evaluatedPath, _config.DepthTurns, _config.RolloutDepth);
            }

            // ── Backprop ──────────────────────────────────────────────────────
            // All nodes are bot-POV; accumulate without sign flip.
            foreach (var n in nodePath)
                n.Update(value);

            // Equivalence-gate instrumentation (null in production — the path
            // key is only built when a trace consumer is attached).
            if (OnIterationTrace is { } trace)
                trace(string.Join(" > ", evaluatedPath.Select(m => m.Key)), value);

            iterations++;
        }

        // ── Robust child selection ─────────────────────────────────────────────
        // Most-visited root child; tie-break by higher mean value.
        var best = rootNode.Children
            .OrderByDescending(c => c.Visits)
            .ThenByDescending(c => c.MeanValue)
            .First()
            .IncomingMove!;

        // ── Root-child stats ───────────────────────────────────────────────────
        // One entry per expanded root child carrying a move (the root node itself
        // has a null IncomingMove and is excluded).
        var rootStats = rootNode.Children
            .Where(c => c.IncomingMove is not null)
            .Select(c => new RootStat(c.IncomingMove!, c.Visits, c.TotalValue))
            .ToList();

        return new SearchResult(best, rootStats);
    }

    // ── Tree-state reuse descent helpers ──────────────────────────────────────

    /// <summary>
    /// Walks <paramref name="nodePath"/> (root → current) backwards to the
    /// nearest node carrying a cached state in a turn at or below
    /// <paramref name="horizonTurn"/>, and returns that cache together with
    /// the number of moves from the root it sits at — i.e. how many leading
    /// moves of the current path are already "inside" the cache and must NOT
    /// be replayed. The root is always cached under reuse (set in
    /// <see cref="SearchWithStats"/>) and always sits at or below any
    /// horizon, so this always finds one. Expansion passes
    /// <see cref="int.MaxValue"/> (the Advance drive has no meaningful turn
    /// cap); rollouts pass the playout horizon.
    /// </summary>
    private static (IReadOnlyList<Player> Cache, ResumeCtx Ctx, int MovesFromRoot)
        NearestCachedAncestor(List<MctsNode> nodePath, int horizonTurn = int.MaxValue)
    {
        for (var i = nodePath.Count - 1; i >= 0; i--)
        {
            if (nodePath[i] is { CachedPlayers: { } cache, ResumeContext: { } ctx }
                && ctx.TurnNumber <= horizonTurn)
            {
                return (cache, ctx, i);
            }
        }

        throw new InvalidOperationException(
            "No cached ancestor found — the root must always be cached under tree-state reuse.");
    }

    /// <summary>
    /// The move suffix to replay from a cached ancestor sitting
    /// <paramref name="movesFromRoot"/> moves below the root: the remainder
    /// of <paramref name="path"/> past the ancestor, plus the optional
    /// <paramref name="extraMove"/> being expanded. Empty when the cache IS
    /// the current node (the frozen players already sit at the decision).
    /// </summary>
    private static IReadOnlyList<SimMove> SuffixFrom(
        IReadOnlyList<SimMove> path, int movesFromRoot, SimMove? extraMove)
    {
        if (extraMove is null && movesFromRoot == path.Count)
            return Array.Empty<SimMove>();

        var suffix = new List<SimMove>(path.Count - movesFromRoot + (extraMove is null ? 0 : 1));
        for (var i = movesFromRoot; i < path.Count; i++)
            suffix.Add(path[i]);
        if (extraMove is not null)
            suffix.Add(extraMove);
        return suffix;
    }
}
