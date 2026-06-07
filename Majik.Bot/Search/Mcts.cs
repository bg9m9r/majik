using System.Diagnostics;

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

    public Mcts(ISearchSimulator sim, MctsConfig config)
    {
        _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Run UCT from the given root state and return the best (robust-child)
    /// root move.
    /// </summary>
    public SimMove Search(SimState root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // ── Short-circuit ──────────────────────────────────────────────────────
        var rootDecision = _sim.Advance(root, Array.Empty<SimMove>());

        if (rootDecision.IsTerminal)
            throw new InvalidOperationException("Root position is already terminal — no move to return.");

        if (rootDecision.LegalMoves.Count == 1)
            return rootDecision.LegalMoves[0];

        // ── Build root node ────────────────────────────────────────────────────
        var rootNode = new MctsNode(incomingMove: null, rootDecision.LegalMoves);

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
                var childDecision = _sim.Advance(root, childPath);

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
            else if (evaluatedNode.IsFullyExpanded && evaluatedNode.IsLeaf)
            {
                // Truly terminal node (no legal moves, already expanded empty).
                // Run a rollout from this position.
                value = _sim.Rollout(root, evaluatedPath, _config.DepthTurns);
            }
            else
            {
                value = _sim.Rollout(root, evaluatedPath, _config.DepthTurns);
            }

            // ── Backprop ──────────────────────────────────────────────────────
            // All nodes are bot-POV; accumulate without sign flip.
            foreach (var n in nodePath)
                n.Update(value);

            iterations++;
        }

        // ── Robust child selection ─────────────────────────────────────────────
        // Most-visited root child; tie-break by higher mean value.
        return rootNode.Children
            .OrderByDescending(c => c.Visits)
            .ThenByDescending(c => c.MeanValue)
            .First()
            .IncomingMove!;
    }
}
