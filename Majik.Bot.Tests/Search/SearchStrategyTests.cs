using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Builds a minimal GameContext at the Combat phase / DeclareAttackers step
/// for SearchStrategy tests. Mirrors BotTestScenario but with phase=Combat and
/// the given self as active player.
/// </summary>
internal static class SearchTestCtx
{
    public static GameContext AtCombat(Player self, Player opp)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        return new GameContext(
            self: self,
            allPlayers: new[] { self, opp },
            activePlayer: self,
            turnNumber: 3,
            currentPhase: StepStateType.DeclareAttackers,
            stack: stack);
    }
}

public class SearchStrategyTests
{
    [Fact]
    public void SearchStrategy_PicksLethalAttack_OverHoldingBack()
    {
        // Alice: 2x ready 2/2; Bob at 3 life, no blockers. Searching Alice's attack must swing.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 3);

        var bears = new List<Creature>();
        foreach (var n in new[] { "A", "B" })
        {
            var c = new Creature($"Bear{n}", "{1}{G}", 2, 2);
            c.ChangeOwner(alice);
            alice.Zones.Battlefield.AddCard(c);
            c.ClearSummoningSickness();
            bears.Add(c);
        }

        // Pad libraries so the engine does not draw-lose immediately.
        foreach (var _ in Enumerable.Range(0, 15))
        {
            var f = new Land("Forest");
            f.ChangeOwner(alice);
            alice.Zones.GetZone(ZoneType.Library).AddCard(f);
            var g = new Land("Forest");
            g.ChangeOwner(bob);
            bob.Zones.GetZone(ZoneType.Library).AddCard(g);
        }

        var ctx = SearchTestCtx.AtCombat(alice, bob);
        var strat = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts"));

        var plan = strat.PickAttackers(ctx, alice, bears);

        // Swings with both (lethal).
        plan.Attackers.Should().HaveCount(2);
        // LIVE creatures must be returned, not sandbox clones.
        plan.Attackers.Select(a => a.Attacker).Should().OnlyContain(c => bears.Contains(c));
    }
}
