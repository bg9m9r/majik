using FluentAssertions;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

/// <summary>
/// CR 500.7 / CR 720.1 — GameDriver wires the ControlPlayerRegistry's
/// extra-turn-after rider (Emrakul, the Promised End: "After that turn, that
/// player takes an extra turn.") to its ExtraTurnQueue. When a control grant
/// carrying the rider is torn down at the end of the controlled turn
/// (ClearActiveControl), the controlled player's extra turn is enqueued onto
/// the same queue the turn loop drains before round-robin advancement.
/// </summary>
public class GameDriverControlExtraTurnTests
{
    [Fact]
    public void Driver_ControlGrantWithRider_EnqueuesControlledPlayersExtraTurn()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var driver = TestDriver(new[] { alice, bob });

        driver.ExtraTurns.Pending.Should().Be(0);

        // CR 720.1 + CR 500.7 — Alice (Emrakul's controller) takes control of
        // Bob's next turn WITH the extra-turn rider.
        driver.ControlPlayers.GrantControl(
            controller: alice, controlled: bob, extraTurnAfter: true);

        // Simulate Bob's controlled turn lifecycle (TurnDriver does this in a
        // real game): consume at turn-start, clear at turn-end.
        driver.ControlPlayers.ConsumeControlFor(bob, out var controller).Should().BeTrue();
        controller.Should().BeSameAs(alice);
        driver.ControlPlayers.ClearActiveControl();

        // CR 500.7 — Bob's extra turn is now queued via the GameDriver's
        // wiring of ScheduleExtraTurnAfterControl → ExtraTurnQueue.
        driver.ExtraTurns.Pending.Should().Be(1);
    }

    [Fact]
    public void Driver_ControlGrantWithoutRider_DoesNotEnqueueExtraTurn()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var driver = TestDriver(new[] { alice, bob });

        // Mindslaver-style grant — no extra-turn rider.
        driver.ControlPlayers.GrantControl(controller: alice, controlled: bob);
        driver.ControlPlayers.ConsumeControlFor(bob, out _).Should().BeTrue();
        driver.ControlPlayers.ClearActiveControl();

        driver.ExtraTurns.Pending.Should().Be(0,
            "Mindslaver has no extra-turn rider");
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
