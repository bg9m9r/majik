using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RunGameAsync_ExplicitStartingPlayer_TakesFirstTurn_RegardlessOfRng(int slot)
    {
        // CR 103.2 / 103.4 / 103.7 — when the starting player has already
        // been decided upstream (die roll + play/draw choice), the driver
        // MUST honour it rather than re-rolling. We assert this across many
        // RNG seeds: with the random fallback the engine would pick the
        // wrong seat for roughly half the seeds, so a single mismatch fails.
        for (var seed = 0; seed < 40; seed++)
        {
            var alice = new Player("Alice", 20);
            var bob = new Player("Bob", 20);
            Seed(alice, 30);
            Seed(bob, 30);

            var stack = new Majik.Core.Stack.Stack(_bus);
            var triggers = new TriggerManager(stack, _bus);
            var priority = new PriorityManager(new List<Player> { alice, bob }, stack, _bus, triggers);
            var driver = new GameDriver(
                players: new[] { alice, bob },
                agents: new Dictionary<Player, IPlayerAgent>
                {
                    [alice] = new DeterministicBotAgent(),
                    [bob] = new DeterministicBotAgent(),
                },
                stack: stack,
                zoneService: _zones,
                triggerManager: triggers,
                stackResolver: _resolver,
                stateBasedActions: _sba,
                priorityManager: priority,
                combatFlow: new CombatFlow(_bus, _sba),
                rng: new GameRandom(seed));

            var expected = slot == 0 ? alice : bob;
            var result = await driver.RunGameAsync(maxTurns: 1, startingPlayerIndex: slot);

            result.StartingPlayer.Should().BeSameAs(expected,
                because: $"slot {slot} was specified explicitly (seed {seed})");
        }
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
