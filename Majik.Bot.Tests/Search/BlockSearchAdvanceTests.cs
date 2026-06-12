using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Root block search (Task 5): an Advance on a root carrying
/// <see cref="SimState.PreDeclaredAttack"/> with the searched seat = DEFENDER
/// must surface the defender's DeclareBlockers decision against the REAL
/// pre-declared attackers (InstanceId match) — not a re-derived attack.
/// </summary>
public class BlockSearchAdvanceTests
{
    private static (Player Alice, Player Bob, Creature Attacker, Creature Blocker) MidCombatBoard(int bobLife = 20)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", bobLife);

        // Alice's attack is already DECLARED live: attacker tapped.
        var attacker = new Creature("Hill Giant", "{3}{R}", 3, 3);
        attacker.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(attacker);
        attacker.ClearSummoningSickness();
        attacker.Tap();

        // Bob has an untapped potential blocker.
        var blocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        blocker.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(blocker);
        blocker.ClearSummoningSickness();

        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest"); f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest"); g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        return (alice, bob, attacker, blocker);
    }

    [Fact]
    public void Advance_OnPreDeclaredAttackRoot_SurfacesDefenderBlockDecision_AgainstRealAttackers()
    {
        var (alice, bob, attacker, blocker) = MidCombatBoard();

        var root = SimState.Capture(
            new[] { alice, bob },
            activePlayer: alice,                 // the ATTACKING player
            turnNumber: 3,
            phase: PhaseStateType.Combat,
            searchedSeat: bob,                   // the DEFENDER
            preDeclaredAttack: CombatResumeState.FromAttackers(new[] { attacker }, bob));

        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));

        var decision = sim.Advance(root, Array.Empty<SimMove>());

        decision.IsTerminal.Should().BeFalse();
        decision.Kind.Should().Be(SimDecisionKind.DeclareBlockers,
            "the resume must reach the DEFENDER's block ask against the pre-declared attack");

        // The block moves must reference the REAL pre-declared attacker by
        // InstanceId — not a re-derived attack (the live attacker is tapped,
        // so a re-run declaration could never include it).
        var blockingMoves = decision.LegalMoves
            .Where(m => m.BlockPlan is { } p && p.Blockers.Count > 0)
            .ToList();
        blockingMoves.Should().NotBeEmpty("the defender has an untapped eligible blocker");
        blockingMoves.Should().OnlyContain(m =>
            m.BlockPlan!.Blockers.All(b => b.Attacker.InstanceId == attacker.InstanceId));
        blockingMoves.SelectMany(m => m.BlockPlan!.Blockers)
            .Should().OnlyContain(b => b.Blocker.InstanceId == blocker.InstanceId);
    }

    [Fact]
    public void Rollout_OnPreDeclaredAttackRoot_CompletesAndScores()
    {
        var (alice, bob, attacker, _) = MidCombatBoard(bobLife: 3); // the unblocked 3/3 is lethal

        var root = SimState.Capture(
            new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            phase: PhaseStateType.Combat,
            searchedSeat: bob,
            preDeclaredAttack: CombatResumeState.FromAttackers(new[] { attacker }, bob));

        var sim = new EngineSimulator(ArchetypeWeights.ForArchetype("Burn"));
        var decision = sim.Advance(root, Array.Empty<SimMove>());
        decision.Kind.Should().Be(SimDecisionKind.DeclareBlockers);

        // Letting the lethal attack through must roll out to a LOSS for the
        // searched defender; blocking it must score strictly better.
        var noBlock = decision.LegalMoves.First(m =>
            m.BlockPlan is { } p && p.Blockers.Count == 0);
        var block = decision.LegalMoves.First(m =>
            m.BlockPlan is { } p && p.Blockers.Count > 0);

        var noBlockValue = sim.Rollout(root, new[] { noBlock }, depthTurns: 0);
        var blockValue = sim.Rollout(root, new[] { block }, depthTurns: 0);

        blockValue.Should().BeGreaterThan(noBlockValue,
            "chump-blocking the lethal attacker keeps the defender alive");
    }
}
