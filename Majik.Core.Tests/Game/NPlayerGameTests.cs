using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

public class NPlayerGameTests
{
    [Fact]
    public async Task FourPlayerGame_RunsThreeTurns_NoExceptions()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var players = Enumerable.Range(0, 4)
            .Select(i => new Player($"P{i}", 40))
            .ToList();
        var priority = new PriorityManager(players, stack, bus, triggers);
        var combat = new CombatFlow(bus, sba);

        var agents = players.ToDictionary(
            p => p, p => (IPlayerAgent)new DeterministicBotAgent());

        foreach (var p in players)
        {
            for (var i = 0; i < 30; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library);
            }
        }

        var driver = new GameDriver(
            players, agents, stack, zones, triggers, resolver, sba,
            priority, combat, new GameRandom(7));

        var result = await driver.RunGameAsync(maxTurns: 3);

        result.TurnsPlayed.Should().Be(3);
        result.StartingPlayer.Should().NotBeNull();
        players.Should().Contain(result.StartingPlayer!);
    }

    [Fact]
    public async Task ThreePlayer_PriorityLoop_AllPassEndsRound()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var priority = new PriorityManager(
            new List<Player> { new("A", 20), new("B", 20), new("C", 20) },
            stack, bus, triggers);

        var players = new[] { new Player("A", 20), new Player("B", 20), new Player("C", 20) };
        var actualPriority = new PriorityManager(players.ToList(), stack, bus, triggers);
        var agents = players.ToDictionary(
            p => p, p => (IPlayerAgent)new DeterministicBotAgent());
        var loop = new PriorityLoop(
            players, actualPriority, stack, resolver, zones, agents,
            () => 1, () => Majik.Core.StateMachine.PhaseStateType.Main);

        await loop.RunUntilRoundEndsAsync(players[0]);

        stack.IsEmpty.Should().BeTrue();
    }
}
