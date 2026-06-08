using FluentAssertions;
using Majik.Bot.Search;
using Xunit;

namespace Majik.Bot.Tests.Search;

public class MctsNodeTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal SimMove with only the Key set, for use in tree tests
    /// that don't need a real engine.
    /// </summary>
    private static SimMove Move(string key) => SimMove.ForTest(key);

    // ── UCB1 selection ───────────────────────────────────────────────────────

    [Fact]
    public void Ucb1_PrefersHigherMean_WhenVisitsEqual()
    {
        var root = new MctsNode(null, Array.Empty<SimMove>());
        var a = root.AddChild(Move("a"), Array.Empty<SimMove>());
        a.Update(1.0);
        a.Update(1.0);
        var b = root.AddChild(Move("b"), Array.Empty<SimMove>());
        b.Update(0.0);
        b.Update(0.0);
        // parent visits = 4 (matches a.Visits + b.Visits so UCB exploration term is equal for both)
        root.Update(0); root.Update(0); root.Update(0); root.Update(0);

        root.SelectChildUcb1(1.41).Should().BeSameAs(a);
    }

    [Fact]
    public void Ucb1_PrefersUnvisited_OverVisitedChild()
    {
        var root = new MctsNode(null, new[] { Move("visited"), Move("unvisited") });

        // Expand both children manually following dequeue protocol.
        var m1 = root.UntriedMoves.Dequeue();
        var visited = root.AddChild(m1, Array.Empty<SimMove>());
        visited.Update(0.5);
        root.Update(0); // parent visit so unvisited exploration term → infinity

        var m2 = root.UntriedMoves.Dequeue();
        var unvisited = root.AddChild(m2, Array.Empty<SimMove>());

        // unvisited child has 0 visits => UCB1 exploration term is +infinity
        root.SelectChildUcb1(1.41).Should().BeSameAs(unvisited);
    }

    // ── AddChild / IsFullyExpanded ────────────────────────────────────────────

    [Fact]
    public void AddChild_MovesFromUntried_ToChildren()
    {
        var moves = new[] { Move("m1"), Move("m2") };
        var root = new MctsNode(null, moves);

        root.IsFullyExpanded.Should().BeFalse();
        root.IsLeaf.Should().BeTrue(); // no children yet

        // Caller dequeues before passing to AddChild (matches Mcts.Search protocol).
        var move = root.UntriedMoves.Dequeue();
        var child = root.AddChild(move, Array.Empty<SimMove>());

        root.Children.Should().ContainSingle().Which.Should().BeSameAs(child);
        child.IncomingMove!.Key.Should().Be("m1");
        // One untried left — not fully expanded.
        root.IsFullyExpanded.Should().BeFalse();
        root.IsLeaf.Should().BeFalse();
    }

    [Fact]
    public void AddChild_AllMoves_MakesNodeFullyExpanded()
    {
        var moves = new[] { Move("x") };
        var root = new MctsNode(null, moves);

        // Caller dequeues the move before passing it to AddChild (matches Mcts.Search protocol).
        var move = root.UntriedMoves.Dequeue();
        root.AddChild(move, Array.Empty<SimMove>());

        root.IsFullyExpanded.Should().BeTrue();
    }

    // ── Update / MeanValue ───────────────────────────────────────────────────

    [Fact]
    public void Update_AccumulatesCorrectly()
    {
        var node = new MctsNode(null, Array.Empty<SimMove>());

        node.Visits.Should().Be(0);
        node.MeanValue.Should().Be(0.0);

        node.Update(0.8);
        node.Update(0.4);

        node.Visits.Should().Be(2);
        node.TotalValue.Should().BeApproximately(1.2, 1e-10);
        node.MeanValue.Should().BeApproximately(0.6, 1e-10);
    }
}
