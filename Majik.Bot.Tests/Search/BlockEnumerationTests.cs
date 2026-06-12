using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Builds a minimal GameContext with the DEFENDER as the searched seat and
/// the ATTACKER as active player (blocking happens on the attacker's turn).
/// </summary>
internal static class BlockSearchTestCtx
{
    public static GameContext AtBlock(Player defender, Player attacker)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        return new GameContext(
            self: defender,
            allPlayers: new[] { attacker, defender },
            activePlayer: attacker,          // attacker's turn — blocking happens here
            turnNumber: 3,
            currentPhase: StepStateType.DeclareBlockers,
            stack: stack);
    }
}

/// <summary>
/// Behavioural tests for the enriched block enumeration added in Task D1.5.
/// Exercises <see cref="SearchStrategy.PickBlockers"/> end-to-end through
/// MCTS so that the legal-move pool (chump blocks, trades, gang blocks)
/// actually affects the chosen plan.
/// </summary>
public class BlockEnumerationTests
{
    // ── Helper ───────────────────────────────────────────────────────────────

    private static IEnumerable<BlockerDeclaration> BlockAssignmentsOf(BlockPlan plan)
        => plan.Blockers;

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bob at 3 life, Alice attacks with a 5/5. Bob's only creature is a 1/1.
    /// The 1/1 cannot survive (chump block), but blocking IS correct: unblocked,
    /// the 5/5 deals 5 damage and kills Bob. Chump-blocking reduces damage to 0
    /// (the blocker absorbs the 5-power attack and dies to the 5/5's toughness
    /// check) and keeps Bob alive.
    ///
    /// The pre-enrichment greedy enumerator only offered no-block + a survive-only
    /// hard-block (which skips the 1/1 because 1 toughness ≤ 5 power), so the
    /// search had only one option (no-block = lose) and couldn't find the chump.
    /// After enrichment the chump is in the legal-move set and MCTS picks it.
    /// </summary>
    [Fact]
    public void Search_ChumpBlocks_ToSurviveOtherwiseLethal()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);

        // Alice's attacker: 5/5 — lethal vs Bob at 3 life if unblocked.
        var bigAtt = new Creature("HillGiant", "{3}{R}", 5, 5);
        bigAtt.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(bigAtt);
        bigAtt.ClearSummoningSickness();

        // Bob's only blocker: 1/1 — won't survive but can absorb damage.
        var chump = new Creature("Goblin", "{R}", 1, 1);
        chump.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(chump);

        // Pad libraries so the engine never auto-draws-out.
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var m = new Land("Mountain"); m.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(m);
            var g = new Land("Mountain"); g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        // Searching seat = Bob (the defender); active player = Alice (attacker's turn).
        var ctx = BlockSearchTestCtx.AtBlock(defender: bob, attacker: alice);
        var strat = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts"));

        var plan = strat.PickBlockers(ctx, bob,
            attackers: new[] { bigAtt },
            eligible: new[] { chump });

        plan.Should().NotBeNull();
        // The 1/1 must be assigned to block the 5/5 (chump block to survive).
        BlockAssignmentsOf(plan).Should().Contain(
            a => a.Blocker.InstanceId == chump.InstanceId,
            because: "chumping the 5/5 is the only way Bob survives — the greedy " +
                     "survive-only blocker would never assign the 1/1, so finding " +
                     "this plan proves the enriched enumeration is working");
    }

    /// <summary>
    /// Bob at 20 life, Alice attacks with a 2/2. Bob has a 1/1. There is no
    /// urgency to chump — taking 2 is fine. The search should not over-aggressively
    /// chump-block when it is clearly losing board-equity for no life-total gain.
    ///
    /// This guards the enrichment against always-chump pathology: when the blocking
    /// player is safe, no-block must remain a valid (and preferred) choice.
    /// </summary>
    [Fact]
    public void Search_DoesNotChump_WhenSafe()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var att22 = new Creature("GrizzlyBears", "{1}{G}", 2, 2);
        att22.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(att22);
        att22.ClearSummoningSickness();
        // Board fidelity at the block prompt: a declared attacker is TAPPED
        // (CR 508.1f). The root block search simulates the following turns,
        // where an untapped "attacker" would unrealistically be free to block
        // Bob's counterattack — skewing the chump/no-chump comparison.
        att22.Tap();

        var chump = new Creature("Goblin", "{R}", 1, 1);
        chump.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(chump);

        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest"); f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest"); g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var ctx = BlockSearchTestCtx.AtBlock(defender: bob, attacker: alice);
        var strat = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts"));

        var plan = strat.PickBlockers(ctx, bob,
            attackers: new[] { att22 },
            eligible: new[] { chump });

        // At 20 life taking 2 is trivially safe; the 1/1 dies for nothing.
        // The search should choose no-block (plan is empty) rather than throwing
        // away the only blocker for 2 damage prevention.
        plan.Blockers.Should().BeEmpty(
            because: "Bob at 20 life has no reason to throw away his only creature " +
                     "to stop 2 damage from a 2/2 — no-block is clearly better");
    }

    /// <summary>
    /// Verify that <see cref="SearchAgent"/> surfaces chump-block options in the
    /// enumerated legal moves (unit-level check, independent of MCTS search quality).
    /// Bob at 3 life, Alice attacks with a 5/5, Bob has a 1/1.
    /// The DeclareBlockers legal-move set must include an assignment of the 1/1
    /// to the 5/5 — this is the enrichment this task adds.
    /// </summary>
    [Fact]
    public async Task SearchAgent_BlockLegalMoves_IncludesChumpBlock()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);

        var bigAtt = new Creature("HillGiant", "{3}{R}", 5, 5);
        bigAtt.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(bigAtt);
        bigAtt.ClearSummoningSickness();

        var chump = new Creature("Goblin", "{R}", 1, 1);
        chump.ChangeOwner(bob);
        bob.Zones.Battlefield.AddCard(chump);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(
            self: bob,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 3,
            currentPhase: StepStateType.DeclareBlockers,
            stack: stack);

        // Call DeclareBlockersAsync directly on the agent and inspect legal moves.
        var agent = new SearchAgent(bob);
        var attackers = new[] { bigAtt };
        var eligible = new[] { chump };

        // Kick the agent on a background thread so we can read the decision.
        var engineTask = Task.Run(async () =>
            await agent.DeclareBlockersAsync(ctx, attackers, eligible));

        var decision = await agent.NextDecisionAsync().WaitAsync(TimeSpan.FromSeconds(3));

        decision.Kind.Should().Be(SimDecisionKind.DeclareBlockers);

        // The chump block must appear in the legal moves.
        decision.LegalMoves.Should().Contain(
            m => m.BlockPlan != null && m.BlockPlan.Blockers.Count > 0 &&
                 m.BlockPlan.Blockers.Any(b => b.Blocker.InstanceId == chump.InstanceId),
            because: "enriched enumeration must include chump-block (1/1 blocks 5/5) " +
                     "even though the blocker dies");

        // No-block must always be present.
        decision.LegalMoves.Should().Contain(
            m => m.BlockPlan != null && m.BlockPlan.Blockers.Count == 0,
            because: "no-block is always a legal option");

        // Reply so the engine task doesn't hang.
        agent.SupplyMove(decision.LegalMoves[0]);
        await engineTask.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
