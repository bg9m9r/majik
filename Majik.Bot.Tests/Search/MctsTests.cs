using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

public class MctsTests
{
    [Fact]
    public void Mcts_FindsLethalSwing_AsBestRootMove()
    {
        // Reuse the Stage-A gate board: Alice 2x 2/2 ready, Bob at 3 life, no blockers.
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

        // Resume at Combat (the engine handles BeginningOfCombat priority window
        // then surfaces DeclareAttackers — same board used in Stage-A tests).
        var root = SimState.Capture(
            new[] { alice, bob },
            alice,
            3,
            PhaseStateType.Combat,
            searchedSeat: alice);

        var mcts = new Mcts(
            new EngineSimulator(ArchetypeWeights.ForArchetype("Burn")),
            new MctsConfig(MaxIterations: 120, DepthTurns: 0, ExplorationC: 1.41));

        var best = mcts.Search(root);
        best.IsAllOutAttack.Should().BeTrue("swinging for lethal is the only winning move");
    }
}
