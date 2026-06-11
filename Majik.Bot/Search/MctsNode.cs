using Majik.Core.Players;

namespace Majik.Bot.Search;

/// <summary>
/// A node in the UCT MCTS tree. Each node corresponds to a game state reachable
/// from the search root via a sequence of moves (<see cref="IncomingMove"/> is
/// the last move in that sequence).
///
/// <para>
/// Children represent positions reachable by one additional move. Untried moves
/// are moves that have not yet been expanded into child nodes.
/// </para>
/// </summary>
internal sealed class MctsNode
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>The move that led to this node from its parent (null at the root).</summary>
    public SimMove? IncomingMove { get; }

    // ── Tree-state reuse cache (Task 2 fields; set by the Mcts descent in
    //    Task 3 — nothing reads them yet, zero behavior change) ──────────────

    /// <summary>
    /// Frozen players at this node's decision point (a defensive
    /// <c>GameStateCloner.Clone</c> taken when the node's position was
    /// cache-eligible — see <see cref="SnapshotPolicy"/>), or null when the
    /// node is not cached (ineligible position, or not yet expanded via the
    /// reuse path). Per-search lifetime; never shared across nodes.
    /// </summary>
    internal IReadOnlyList<Player>? CachedPlayers { get; set; }

    /// <summary>
    /// Resume context paired with <see cref="CachedPlayers"/> (turn / phase /
    /// active seat / per-seat land drops + the suffix replayed from the
    /// nearest cached ancestor). Null iff <see cref="CachedPlayers"/> is null.
    /// </summary>
    internal ResumeCtx? ResumeContext { get; set; }

    // ── Statistics ────────────────────────────────────────────────────────────

    /// <summary>How many times this node has been visited during search.</summary>
    public int Visits { get; private set; }

    /// <summary>Accumulated value (bot-POV; higher = better) across all visits.</summary>
    public double TotalValue { get; private set; }

    /// <summary>Average value across all visits; 0 when unvisited.</summary>
    public double MeanValue => Visits == 0 ? 0.0 : TotalValue / Visits;

    // ── Tree structure ────────────────────────────────────────────────────────

    /// <summary>Expanded children, in the order they were added.</summary>
    public List<MctsNode> Children { get; } = new();

    /// <summary>Legal moves that have not yet been expanded into child nodes.</summary>
    public Queue<SimMove> UntriedMoves { get; }

    /// <summary>True when every legal move has been expanded into a child.</summary>
    public bool IsFullyExpanded => UntriedMoves.Count == 0;

    /// <summary>True when no children have been added yet.</summary>
    public bool IsLeaf => Children.Count == 0;

    // ── Construction ──────────────────────────────────────────────────────────

    public MctsNode(SimMove? incomingMove, IEnumerable<SimMove> legalMoves)
    {
        IncomingMove = incomingMove;
        UntriedMoves = new Queue<SimMove>(legalMoves);
    }

    // ── Tree operations ───────────────────────────────────────────────────────

    /// <summary>
    /// Dequeue the <paramref name="move"/> from untried moves, create a child
    /// node for it with the given legal moves, attach it, and return it.
    /// </summary>
    /// <param name="move">The move being expanded (must be the next untried move).</param>
    /// <param name="childLegalMoves">Legal moves available from the new child's position.</param>
    public MctsNode AddChild(SimMove move, IEnumerable<SimMove> childLegalMoves)
    {
        // The caller is responsible for calling UntriedMoves.Dequeue() via Search;
        // here we just build the child and attach it (the move was already dequeued
        // by Mcts.Search before calling AddChild).
        var child = new MctsNode(move, childLegalMoves);
        Children.Add(child);
        return child;
    }

    /// <summary>
    /// Record one simulation result: increments <see cref="Visits"/> and adds
    /// <paramref name="value"/> to <see cref="TotalValue"/>.
    /// </summary>
    public void Update(double value)
    {
        Visits++;
        TotalValue += value;
    }

    /// <summary>
    /// UCB1 child selection: returns the child that maximises
    /// <c>mean + C * sqrt(ln(parentVisits) / childVisits)</c>.
    /// Unvisited children (Visits == 0) are treated as +∞ and selected first.
    /// </summary>
    /// <param name="explorationC">Exploration constant (typically √2 ≈ 1.41).</param>
    public MctsNode SelectChildUcb1(double explorationC)
    {
        if (Children.Count == 0)
            throw new InvalidOperationException("Cannot select a child from a leaf node.");

        var logParent = Math.Log(Visits);

        MctsNode? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var child in Children)
        {
            double score;
            if (child.Visits == 0)
            {
                // Unvisited child has infinite exploration value — always prefer it.
                score = double.PositiveInfinity;
            }
            else
            {
                score = child.MeanValue + explorationC * Math.Sqrt(logParent / child.Visits);
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = child;
            }
        }

        return best!;
    }
}
