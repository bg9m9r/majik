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

public class GameDriverPhase25Tests
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

    public GameDriverPhase25Tests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public async Task RunGameAsync_DealsSevenCardHand_AfterMulligan()
    {
        Seed(_alice, 30);
        Seed(_bob, 30);
        var driver = NewDriver(rngSeed: 1);

        await driver.RunGameAsync(maxTurns: 1);

        _alice.Zones.Hand.Count.Should().Be(7);
        _bob.Zones.Hand.Count.Should().Be(7);
    }

    [Fact]
    public async Task SameSeed_ProducesSameStartingPlayer()
    {
        Seed(_alice, 30);
        Seed(_bob, 30);
        var r1 = await NewDriver(rngSeed: 42).RunGameAsync(maxTurns: 1);

        // Reset for second run
        var alice2 = new Player("Alice", 20);
        var bob2 = new Player("Bob", 20);
        for (var i = 0; i < 30; i++)
        {
            var ca = NamedCardFactory.Create("Mountain", alice2);
            alice2.Zones.Library.AddCard(ca); ca.Zone = ZoneType.Library;
            var cb = NamedCardFactory.Create("Mountain", bob2);
            bob2.Zones.Library.AddCard(cb); cb.Zone = ZoneType.Library;
        }
        var stack2 = new Majik.Core.Stack.Stack(_bus);
        var trig2 = new TriggerManager(stack2, _bus);
        var pri2 = new PriorityManager(new List<Player> { alice2, bob2 }, stack2, _bus, trig2);
        var driver2 = new GameDriver(
            new[] { alice2, bob2 },
            new Dictionary<Player, IPlayerAgent>
            {
                [alice2] = new DeterministicBotAgent(),
                [bob2] = new DeterministicBotAgent(),
            },
            stack2, _zones, trig2, _resolver, _sba, pri2,
            new CombatFlow(_bus, _sba), new GameRandom(42));
        var r2 = await driver2.RunGameAsync(maxTurns: 1);

        r2.StartingPlayer!.Name.Should().Be(r1.StartingPlayer!.Name);
    }

    private GameDriver NewDriver(int rngSeed)
    {
        return new GameDriver(
            new[] { _alice, _bob },
            new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = new DeterministicBotAgent(),
                [_bob] = new DeterministicBotAgent(),
            },
            _stack, _zones, _triggers, _resolver, _sba, _priority,
            new CombatFlow(_bus, _sba),
            new GameRandom(rngSeed));
    }

    private static void Seed(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c); c.Zone = ZoneType.Library;
        }
    }
}
