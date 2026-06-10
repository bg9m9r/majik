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
/// Tests for <see cref="DeterminizedSearch.RunBelief"/> — the belief-driven entry
/// that samples a DIFFERENT opponent decklist per world (one block of worlds per
/// archetype in the allocation), threads a per-world observed-public list through,
/// and votes by the same summed-robust-child as <see cref="DeterminizedSearch.Run"/>.
/// </summary>
public class RunBeliefTests
{
    /// <summary>
    /// Lethal-swing board: Alice has 2x 2/2 ready, Bob is at 3 life with no blockers,
    /// resumed at Combat. Swinging all-out is lethal regardless of which opponent
    /// hand/library gets sampled, so the all-out attack dominates across every world.
    /// This is the PLAIN capture root — NOT pre-determinized; RunBelief stamps each
    /// world's own decklist.
    /// </summary>
    private static SimState BuildPlainDominantRoot()
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
            new MctsConfig(MaxIterations: 80, DepthTurns: 0, ExplorationC: 1.41));

    [Fact]
    public void RunBelief_AllocatesAcrossArchetypes_VotesDominantMove()
    {
        var mcts = BuildMcts();
        var baseRoot = BuildPlainDominantRoot();

        var best = DeterminizedSearch.RunBelief(mcts, baseRoot,
            new[] { ((IReadOnlyList<string>)BotDeckCatalog.Get("Burn"), 2),
                    ((IReadOnlyList<string>)BotDeckCatalog.Get("Prowess"), 1) },
            observedPublic: null, totalBudgetMs: 1200, perWorldBudgetMs: 400, kMax: 8);

        best.IsAllOutAttack.Should().BeTrue(
            "swinging all-out is lethal across every sampled opponent world");
    }

    [Fact]
    public void RunBelief_SingleArchetype_MatchesRun()
    {
        var viaBelief = DeterminizedSearch.RunBelief(BuildMcts(), BuildPlainDominantRoot(),
            new[] { ((IReadOnlyList<string>)BotDeckCatalog.Get("Burn"), 3) },
            observedPublic: null, totalBudgetMs: 1200, perWorldBudgetMs: 400, kMax: 8);

        var viaRun = DeterminizedSearch.Run(BuildMcts(),
            BuildPlainDominantRoot().WithDeterminization(BotDeckCatalog.Get("Burn"), worldSeed: 0),
            totalBudgetMs: 1200, perWorldBudgetMs: 400, kMax: 8);

        viaBelief.Key.Should().Be(viaRun.Key);
    }

    [Fact]
    public void RunBelief_EmptyAllocation_Throws()
    {
        var mcts = BuildMcts();
        var baseRoot = BuildPlainDominantRoot();

        Action act = () => DeterminizedSearch.RunBelief(mcts, baseRoot,
            System.Array.Empty<(IReadOnlyList<string>, int)>(), null, 1200);

        act.Should().Throw<System.ArgumentException>();
    }
}
