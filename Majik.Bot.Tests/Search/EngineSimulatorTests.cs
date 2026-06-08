using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

public class EngineSimulatorTests
{
    [Fact]
    public void Simulator_Advance_GivesAttackDecision_AndRollout_RewardsLethalSwing()
    {
        // Alice: enough power on board to kill Bob if unblocked; Bob: no blockers, low life.
        var alice = new Player("Alice", 20); var bob = new Player("Bob", 3);
        foreach (var n in new[]{"A","B"}) { var c = new Creature($"Bear{n}", "{1}{G}", 2,2); c.ChangeOwner(alice); alice.Zones.Battlefield.AddCard(c); c.ClearSummoningSickness(); }
        foreach (var _ in Enumerable.Range(0,15)) { var f=new Land("Forest"); f.ChangeOwner(alice); alice.Zones.GetZone(ZoneType.Library).AddCard(f); var g=new Land("Forest"); g.ChangeOwner(bob); bob.Zones.GetZone(ZoneType.Library).AddCard(g); }

        var root = SimState.Capture(new[]{alice,bob}, activePlayer: alice, turnNumber: 3, phase: PhaseStateType.Combat, searchedSeat: alice);
        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));

        var decision = sim.Advance(root, System.Array.Empty<SimMove>());
        decision.IsTerminal.Should().BeFalse();
        decision.Kind.Should().Be(SimDecisionKind.DeclareAttackers);

        var allOut = decision.LegalMoves.First(m => m.IsAllOutAttack);
        // depthTurns: 0 = only the current partial turn (turn 3, starting at
        // DeclareAttackers) is played out; no additional full turns are run.
        // Swing: 2x 2/2 hits Bob (3 life) => 4 damage => Bob dies this turn => WinValue.
        // Pass: no attack this turn => turn ends, both players alive => BoardEval score (< WinValue).
        var swingValue = sim.Rollout(root, new[]{ allOut }, depthTurns: 0);
        var passValue  = sim.Rollout(root, new[]{ decision.LegalMoves.First(m => m.IsEmptyAttack) }, depthTurns: 0);
        swingValue.Should().BeGreaterThan(passValue);   // swinging (4 dmg vs 3 life => win) beats doing nothing
    }
}
