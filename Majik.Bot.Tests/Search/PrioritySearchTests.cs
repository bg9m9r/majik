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
/// Tests that verify MCTS-driven priority decisions.
/// Task D2: drive main-phase PRIORITY decisions via MCTS.
/// </summary>
public class PrioritySearchTests
{
    /// <summary>
    /// Simplest provable priority decision: bot has a land in hand on its main
    /// phase with empty board. Playing the land strictly improves mana
    /// (BoardEval.ManaSources) → search should choose PlayLand over Pass.
    /// Also verifies the InstanceId remap: the returned action must reference
    /// the LIVE land object (same InstanceId), not a sandbox clone.
    /// </summary>
    [Fact]
    public void SearchStrategy_PlaysLand_WhenItHasOne_ViaSearch()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // The land we will verify is returned by InstanceId.
        var landInHand = new Land("Forest");
        landInHand.ChangeOwner(alice);
        alice.Zones.Hand.AddCard(landInHand);

        // Pad libraries so the sandbox engine does not draw-lose immediately.
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);

            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var ctx = PrioritySearchTestCtx.AtMain(alice, bob);
        var strat = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts"));

        var action = strat.PickPriorityAction(ctx, alice);

        action.Should().BeOfType<PriorityAction.PlayLand>(
            because: "playing a land is strictly better than passing when mana sources > 0");
        var playLand = (PriorityAction.PlayLand)action;
        playLand.Land.InstanceId.Should().Be(landInHand.InstanceId,
            because: "the remap must return the LIVE land object, not a sandbox clone");
    }

    /// <summary>
    /// Short-circuit adaptivity guard: when there is nothing to do (empty hand,
    /// empty board) the only legal action is Pass — search should NOT be run,
    /// and the result is Pass.
    /// </summary>
    [Fact]
    public void SearchStrategy_PassesWhenNothingToDo_WithoutSearching()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Empty hand and board — only Pass is legal.
        // Pad libraries.
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);

            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var ctx = PrioritySearchTestCtx.AtMain(alice, bob);
        var strat = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts"));

        var action = strat.PickPriorityAction(ctx, alice);

        action.Should().BeOfType<PriorityAction.PassAction>(
            because: "with no legal actions other than Pass, the short-circuit must return Pass immediately");
    }
}

/// <summary>
/// Builds a minimal GameContext at PreCombatMain for SearchStrategy / priority tests.
/// Self is active, sorcery window is open (empty stack, land play available).
/// </summary>
internal static class PrioritySearchTestCtx
{
    public static GameContext AtMain(Player self, Player opp)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        return new GameContext(
            self: self,
            allPlayers: new[] { self, opp },
            activePlayer: self,
            turnNumber: 3,
            currentPhase: StepStateType.PreCombatMain,
            stack: stack,
            landPlayAvailable: true);
    }
}
