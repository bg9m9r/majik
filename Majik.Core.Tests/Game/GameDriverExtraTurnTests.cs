using FluentAssertions;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

public class GameDriverExtraTurnTests
{
    // Focused unit-style test against the ExtraTurnQueue exposed via
    // GameDriver. Full end-to-end GameDriver wiring (libraries, agents,
    // etc.) is covered elsewhere; this test isolates the new property.

    [Fact]
    public void GameDriver_ExposesExtraTurnQueue_ForEffectIntegration()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var driver = TestDriver(new[] { alice, bob });

        driver.ExtraTurns.Should().NotBeNull();
        driver.ExtraTurns.Pending.Should().Be(0);

        driver.ExtraTurns.EnqueueExtraTurn(alice);
        driver.ExtraTurns.Pending.Should().Be(1);
    }

    private static GameDriver TestDriver(IReadOnlyList<Player> players)
    {
        var stack = new Majik.Core.Stack.Stack();
        var bus = new Majik.Core.Events.EventBus();
        var zoneService = new Majik.Core.Services.ZoneService(bus);
        var triggerManager = new Majik.Core.Abilities.TriggerManager(stack, bus);
        var stackResolver = new Majik.Core.Services.StackResolver(bus, zoneService);
        var sba = new Majik.Core.Rules.StateBasedActions(eventBus: bus, zoneService: zoneService, triggerManager: triggerManager);
        var priority = new Majik.Core.Game.PriorityManager(players.ToList(), stack, bus, triggerManager);
        var combat = new Majik.Core.Combat.CombatFlow(bus, sba);
        var agents = players.ToDictionary<Player, Player, Majik.Core.Players.Agents.IPlayerAgent>(
            p => p, p => new Majik.Core.Players.Agents.DeterministicBotAgent());
        return new GameDriver(players, agents, stack, zoneService, triggerManager,
            stackResolver, sba, priority, combat);
    }
}
