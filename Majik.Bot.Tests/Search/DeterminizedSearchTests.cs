using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Tests for <see cref="DeterminizedSearch"/> — the adaptive K-world driver that
/// searches K independently-sampled worlds of a determinized root and votes by
/// summed robust-child (most total root-child visits across worlds; tie-break by
/// summed mean value). K is a pure function of the two budget ints.
/// </summary>
public class DeterminizedSearchTests
{
    // ── KFor table ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1600, 400, 8, 4)]   // 1600/400 = 4
    [InlineData(100, 400, 8, 1)]    // clamp low: round(0.25) = 0 → clamp to 1
    [InlineData(99999, 400, 8, 8)]  // clamp high: 250 → clamp to 8
    [InlineData(400, 400, 8, 1)]    // 400/400 = 1
    public void KFor_ComputesAdaptiveClampedK(int totalMs, int perWorldMs, int kMax, int expected)
    {
        DeterminizedSearch.KFor(totalMs, perWorldMs, kMax).Should().Be(expected);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stage-A lethal-swing board: Alice has 2x 2/2 ready, Bob is at 3 life with no
    /// blockers, resumed at Combat. Swinging all-out is lethal regardless of which
    /// opponent hand gets sampled (Bob has no untapped blockers and no instant-speed
    /// removal on the battlefield to stop 4 damage to a 3-life player), so the
    /// all-out attack dominates across every determinized world. The opponent
    /// decklist (Burn) seeds hidden-zone resampling.
    /// </summary>
    private static SimState BuildDominantMoveDeterminizedRoot(int baseSeed)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);
        foreach (var n in new[] { "A", "B" })
        {
            var c = new Creature($"Bear{n}", "{1}{G}", 2, 2);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
        }
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var root = SimState.Capture(
            new[] { alice, bob },
            alice,
            3,
            PhaseStateType.Combat,
            searchedSeat: alice);

        // Determinize against the Burn archetype so hidden zones resample per world.
        return root.WithDeterminization(BotDeckCatalog.Get("Burn"), baseSeed);
    }

    /// <summary>
    /// A forced root: the searched seat has a single legal move. Bob is the active
    /// player with one attacker; Alice (the searched, defending seat) controls no
    /// creatures, so her only DeclareBlockers move is the empty block. That single
    /// legal move makes every world short-circuit in <see cref="Mcts.SearchWithStats"/>
    /// (RootStats = one Visits=0 entry), so the summed-visits tally is all-zero and
    /// the zero-visit fallback must return the forced move without an argmax crash.
    /// Determinized so it routes through <see cref="DeterminizedSearch.Run"/>'s
    /// decklist guard.
    /// </summary>
    private static SimState BuildForcedDeterminizedRoot(int baseSeed)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        // Bob attacks with one creature; Alice has none to block with → one move.
        var attacker = new Creature("BobBear", "{1}{G}", 2, 2);
        attacker.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(attacker);
        attacker.ClearSummoningSickness();
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        // Bob is the active player (turn order: bob first), so the searched seat
        // Alice faces a DeclareBlockers decision.
        var root = SimState.Capture(
            new[] { bob, alice },
            bob,
            4,
            PhaseStateType.Combat,
            searchedSeat: alice);

        return root.WithDeterminization(BotDeckCatalog.Get("Burn"), baseSeed);
    }

    private static Mcts BuildMcts() =>
        new Mcts(
            new EngineSimulator(ArchetypeWeights.ForArchetype("Burn")),
            // Small per-world iteration budget so K worlds run fast.
            new MctsConfig(MaxIterations: 80, DepthTurns: 0, ExplorationC: 1.41));

    // ── Run behaviour ────────────────────────────────────────────────────────

    [Fact]
    public void Run_OverDominantBoard_ReturnsDominantMove()
    {
        var root = BuildDominantMoveDeterminizedRoot(baseSeed: 7);

        // 1600/400 = 4 worlds.
        var move = DeterminizedSearch.Run(BuildMcts(), root, totalBudgetMs: 1600);

        move.IsAllOutAttack.Should().BeTrue(
            "swinging all-out is lethal across every sampled opponent world");
    }

    [Fact]
    public void Run_IsDeterministic_SameRootAndBudgets_SameChosenKey()
    {
        var rootA = BuildDominantMoveDeterminizedRoot(baseSeed: 7);
        var rootB = BuildDominantMoveDeterminizedRoot(baseSeed: 7);

        var a = DeterminizedSearch.Run(BuildMcts(), rootA, totalBudgetMs: 1600);
        var b = DeterminizedSearch.Run(BuildMcts(), rootB, totalBudgetMs: 1600);

        a.Key.Should().Be(b.Key);
    }

    [Fact]
    public void Run_OnPerfectInfoRoot_Throws()
    {
        // A perfect-info root (no WorldSeed / OpponentDecklist) must not come here.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var root = SimState.Capture(
            new[] { alice, bob },
            alice,
            3,
            PhaseStateType.PreCombatMain,
            searchedSeat: alice);

        var act = () => DeterminizedSearch.Run(BuildMcts(), root, totalBudgetMs: 1600);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Run_OnForcedRoot_ReturnsTheForcedMove_NoCrash()
    {
        var root = BuildForcedDeterminizedRoot(baseSeed: 7);

        var move = DeterminizedSearch.Run(BuildMcts(), root, totalBudgetMs: 1600);

        // Single legal move = the empty block. The zero-visit fallback must return
        // it without an argmax-over-empty crash.
        move.Should().NotBeNull();
        move.BlockPlan.Should().NotBeNull();
        move.BlockPlan!.Blockers.Should().BeEmpty();
    }
}
