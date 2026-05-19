using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

public class GameDriverTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priority;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GameDriverTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public async Task RunGameAsync_BothBotsPass_RunsMaxTurns_NoExceptions()
    {
        Seed(_alice, 30);
        Seed(_bob, 30);
        var driver = NewDriver();

        var result = await driver.RunGameAsync(maxTurns: 4);

        result.TurnsPlayed.Should().Be(4);
        result.Winner.Should().BeNull();
    }

    [Fact]
    public async Task RunGameAsync_PlayerAt0Life_EndsGame()
    {
        _bob.LoseLife(20); // Bob already at 0 — SBA will tag him on first check
        Seed(_alice, 10);
        Seed(_bob, 10);
        var driver = NewDriver();

        var result = await driver.RunGameAsync(maxTurns: 10);

        result.Winner.Should().BeSameAs(_alice);
        result.TurnsPlayed.Should().BeLessOrEqualTo(2);
    }

    private GameDriver NewDriver()
    {
        return new GameDriver(
            players: new[] { _alice, _bob },
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = new DeterministicBotAgent(),
                [_bob] = new DeterministicBotAgent(),
            },
            stack: _stack,
            zoneService: _zones,
            triggerManager: _triggers,
            stackResolver: _resolver,
            stateBasedActions: _sba,
            priorityManager: _priority,
            combatFlow: new CombatFlow(_bus, _sba));
    }

    private static void Seed(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
