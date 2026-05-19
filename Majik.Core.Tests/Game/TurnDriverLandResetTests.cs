using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

public class TurnDriverLandResetTests
{
    [Fact]
    public async Task LandDropTracker_ResetsOnNewTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);
        var tracker = new LandDropTracker();
        tracker.RecordLandPlayed(alice); // pretend she already played one
        tracker.DropsUsedThisTurn(alice).Should().Be(1);

        // Seed minimal libraries.
        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library);
            }
        }

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = new DeterministicBotAgent(), [bob] = new DeterministicBotAgent() },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            landDropTracker: tracker);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        tracker.DropsUsedThisTurn(alice).Should().Be(0);
    }
}
