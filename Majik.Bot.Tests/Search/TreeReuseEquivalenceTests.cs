using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// THE EQUIVALENCE GATE for tree-state reuse (Task 3): with the same root,
/// the same deterministic simulator and the same iteration count,
/// <see cref="MctsConfig.TreeStateReuse"/> ON must produce the IDENTICAL
/// iteration sequence as OFF — the same evaluated node (path of move Keys)
/// in the same order on every iteration, the same rollout value (exact
/// double) on every iteration, the same chosen move and the same per-root-
/// child statistics. The sim is deterministic, so ANY divergence is a bug
/// in the reuse descent (a snapshot that is not state-faithful, a suffix
/// mis-accumulation, or an eligibility leak).
///
/// <para>Boards cover the spike's shapes: plain main-phase, multi-spell
/// (the BREAK-4 multi-window replay shape: the second cast happens in a
/// LATER priority window after the first resolved, and the stack-laden
/// intermediate node must NOT cache), combat (DeclareAttackers root with
/// cross-turn child drives), and a determinized world (the materialized
/// base IS the root cache).</para>
///
/// <para>Each ON run also asserts the reuse machinery actually engaged
/// (<see cref="Mcts.ReuseExpansions"/> / <see cref="Mcts.ReuseRollouts"/>
/// &gt; 0) so the gate can never pass vacuously with reuse silently off.</para>
/// </summary>
public sealed class TreeReuseEquivalenceTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    // ── Board builders (mirrors AdvanceFromTests / the spike) ─────────────────

    private static void AddMountains(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var m = (Land)NamedCardFactory.Create("Mountain", p);
            m.ChangeController(p);
            p.Zones.Battlefield.AddCard(m);
        }
    }

    private static void PadLibrary(Player p, int count = 20)
    {
        for (var i = 0; i < count; i++)
        {
            var l = new Land("Forest");
            l.ChangeOwner(p);
            p.Zones.GetZone(ZoneType.Library).AddCard(l);
        }
    }

    private static Creature AddReadyCreature(Player p, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness);
        c.ChangeOwner(p);
        p.Zones.Battlefield.AddCard(c);
        c.ClearSummoningSickness();
        return c;
    }

    private static void AddToHand(Player p, string repoCardName)
    {
        var card = new ScryfallCardFactory(Repo).Create(repoCardName, p);
        p.Zones.Hand.AddCard(card);
    }

    private static void AddLandToHand(Player p, string name = "Mountain")
    {
        var land = (Land)NamedCardFactory.Create(name, p);
        p.Zones.Hand.AddCard(land);
    }

    // ── The gate runner ───────────────────────────────────────────────────────

    private sealed record SearchRun(
        SearchResult Result,
        IReadOnlyList<(string Path, double Value)> Trace,
        int ReuseExpansions,
        int ReuseRollouts);

    private static SearchRun Run(SimState root, bool reuse, int iterations)
    {
        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));
        var mcts = new Mcts(sim, new MctsConfig(
            MaxIterations: iterations,
            MaxMillis: 600_000, // iteration-bounded — wall clock must never truncate the comparison
            DepthTurns: 1,
            ExplorationC: 1.41,
            TreeStateReuse: reuse));

        var trace = new List<(string, double)>();
        mcts.OnIterationTrace = (path, value) => trace.Add((path, value));

        var result = mcts.SearchWithStats(root);
        return new SearchRun(result, trace, mcts.ReuseExpansions, mcts.ReuseRollouts);
    }

    /// <summary>
    /// Runs reuse OFF then ON on the SAME root and asserts the identical
    /// iteration sequence, identical chosen move, identical root stats —
    /// and that the ON run genuinely exercised the reuse machinery.
    /// </summary>
    private static void AssertEquivalent(SimState root, int iterations)
    {
        var off = Run(root, reuse: false, iterations);
        var on = Run(root, reuse: true, iterations);

        // Identical iteration sequence: same evaluated node (path key), in
        // order, with the exact same rollout value, every iteration.
        on.Trace.Count.Should().Be(off.Trace.Count,
            "reuse must not change how many iterations complete (iteration-bounded run)");
        for (var i = 0; i < off.Trace.Count; i++)
        {
            on.Trace[i].Path.Should().Be(off.Trace[i].Path,
                $"iteration {i} must evaluate the same node with reuse ON");
            on.Trace[i].Value.Should().Be(off.Trace[i].Value,
                $"iteration {i} must produce the exact same rollout value with reuse ON");
        }

        // Identical decision + identical root statistics.
        on.Result.Best.Key.Should().Be(off.Result.Best.Key);
        var offStats = off.Result.RootStats
            .Select(s => (s.Move.Key, s.Visits, s.TotalValue)).ToList();
        var onStats = on.Result.RootStats
            .Select(s => (s.Move.Key, s.Visits, s.TotalValue)).ToList();
        onStats.Should().Equal(offStats);

        // The gate must never pass vacuously: the ON run really reused.
        on.ReuseExpansions.Should().BeGreaterThan(0,
            "reuse ON must route expansions through AdvanceFrom");
        on.ReuseRollouts.Should().BeGreaterThan(0,
            "reuse ON must launch rollouts from cached node states");
        off.ReuseExpansions.Should().Be(0, "reuse OFF must stay on the root-replay path");
        off.ReuseRollouts.Should().Be(0, "reuse OFF must stay on the root-replay path");
    }

    // ════════════════════════════════════════════════════════════════════════
    // The four boards
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Plain main-phase board: a land drop + a castable bolt.</summary>
    [Fact(Timeout = 240_000)]
    public async Task Equivalence_PlainBoard()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 1);
        AddLandToHand(alice);
        AddToHand(alice, "Lightning Bolt");
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);

        AssertEquivalent(root, iterations: 48);
    }

    /// <summary>
    /// Multi-spell board (the BREAK-4 shape): TWO bolts castable in one main
    /// phase across separate priority windows. The intermediate stack-laden
    /// node is cache-INELIGIBLE, so deeper expansions must fall back to the
    /// nearest cached ancestor with an accumulated suffix.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task Equivalence_MultiSpellBoard()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 2);
        AddToHand(alice, "Lightning Bolt");
        AddToHand(alice, "Lightning Bolt");
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.PreCombatMain, searchedSeat: alice);

        AssertEquivalent(root, iterations: 48);
    }

    /// <summary>
    /// Combat board: a DeclareAttackers root (2 ready creatures → 4 attack
    /// subsets) whose child drives cross the opponent's inline turn.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task Equivalence_CombatBoard()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddReadyCreature(alice, "Grizzly Bears", "{1}{G}", 2, 2);
        AddReadyCreature(alice, "Hill Giant", "{3}{R}", 3, 3);
        AddReadyCreature(bob, "Craw Wurm", "{4}{G}{G}", 5, 5);
        PadLibrary(alice);
        PadLibrary(bob);

        var root = SimState.Capture(
            new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
            phase: PhaseStateType.Combat, searchedSeat: alice);

        AssertEquivalent(root, iterations: 48);
    }

    /// <summary>
    /// Determinized world: the per-world MATERIALIZED base is the root cache
    /// (shared across the OFF and ON runs via the SimState's world cache, so
    /// both search the identical sampled world).
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task Equivalence_DeterminizedWorld()
    {
        await Task.Yield();

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 30);
        AddMountains(alice, 1);
        AddLandToHand(alice);
        AddToHand(alice, "Lightning Bolt");
        PadLibrary(alice);
        for (var i = 0; i < 3; i++) AddLandToHand(bob, "Mountain");
        PadLibrary(bob);

        var deck = Enumerable.Repeat("Forest", 30)
            .Concat(Enumerable.Repeat("Lightning Bolt", 10))
            .ToList();

        var root = SimState.Capture(
                new[] { alice, bob }, activePlayer: alice, turnNumber: 3,
                phase: PhaseStateType.PreCombatMain, searchedSeat: alice)
            .WithDeterminization(deck, worldSeed: 7);

        AssertEquivalent(root, iterations: 48);
    }
}
