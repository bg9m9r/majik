using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Tests for <see cref="Mcts.SearchWithStats"/>, which exposes per-root-child
/// visit/value statistics so the (future) K-world determinization loop can sum
/// visits across worlds and vote. <c>Search</c> delegates to <c>SearchWithStats</c>,
/// so their best-move output must be identical.
/// </summary>
public class MctsStatsTests
{
    /// <summary>
    /// Builds the Stage-A lethal-swing board (Alice 2x 2/2 ready, Bob at 3 life,
    /// no blockers) resumed at Combat — the same multi-move root the existing
    /// <see cref="MctsTests"/> uses. Yields several legal root moves (attacker
    /// subsets) so the tree branches.
    /// </summary>
    private static SimState BuildMultiMoveRoot()
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

        return SimState.Capture(
            new[] { alice, bob },
            alice,
            3,
            PhaseStateType.Combat,
            searchedSeat: alice);
    }

    private static Mcts BuildMcts() =>
        new Mcts(
            new EngineSimulator(ArchetypeWeights.ForArchetype("Burn")),
            new MctsConfig(MaxIterations: 120, DepthTurns: 0, ExplorationC: 1.41));

    [Fact]
    public void SearchWithStats_RootStats_NonEmpty_AndVisitsAccumulate()
    {
        var root = BuildMultiMoveRoot();
        var result = BuildMcts().SearchWithStats(root);

        result.RootStats.Should().NotBeEmpty();
        result.RootStats.Sum(s => s.Visits).Should().BeGreaterThan(0);
    }

    [Fact]
    public void SearchWithStats_Best_EqualsSearch_ForSameRootAndConfig()
    {
        // EngineSimulator uses a fixed seed, so search is fully deterministic:
        // Search and SearchWithStats.Best must select byte-identical moves.
        var bestViaSearch = BuildMcts().Search(BuildMultiMoveRoot());
        var bestViaStats = BuildMcts().SearchWithStats(BuildMultiMoveRoot()).Best;

        bestViaStats.Key.Should().Be(bestViaSearch.Key);
    }

    [Fact]
    public void SearchWithStats_RootStatMoves_AreLegal_AndKeysUnique()
    {
        var root = BuildMultiMoveRoot();
        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));
        var legalKeys = sim.Advance(root, Array.Empty<SimMove>())
            .LegalMoves.Select(m => m.Key).ToHashSet();

        var result = BuildMcts().SearchWithStats(root);

        result.RootStats.Should().OnlyContain(s => legalKeys.Contains(s.Move.Key));

        var keys = result.RootStats.Select(s => s.Move.Key).ToList();
        keys.Should().OnlyHaveUniqueItems();
    }
}
