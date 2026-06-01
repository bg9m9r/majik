using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Counters;

/// <summary>
/// CR 122.1g — Stun counters. "If a permanent with a stun counter on it would
/// become untapped, instead remove a stun counter from it." Exercises the
/// untap-step replacement wired in <see cref="TurnDriver"/>.
/// </summary>
public class StunCounterUntapTests : IDisposable
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

    public StunCounterUntapTests()
    {
        UntapStepRestrictions.Clear();
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    public void Dispose() => UntapStepRestrictions.Clear();

    private TurnDriver NewDriver()
    {
        return new TurnDriver(
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
            combatFlow: new CombatFlow(_bus, _sba),
            eventBus: _bus);
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = Majik.Core.CardData.NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    [Fact]
    public async Task StunCounter_PreventsOneUntap_ThenRemovesCounter()
    {
        // A tapped creature with one stun counter: at its controller's untap
        // step it stays tapped and the stun counter is removed (CR 122.1g).
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Tap();
        bear.Counters.Add(CounterType.Stun, 1);

        SeedLibrary(_alice, 5);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        bear.IsTapped.Should().BeTrue(
            "a permanent with a stun counter does not untap (CR 122.1g)");
        bear.Counters.Count(CounterType.Stun).Should().Be(0,
            "one stun counter is removed instead of untapping");
    }

    [Fact]
    public async Task StunCounter_TwoCounters_NeedTwoUntapSteps()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Tap();
        bear.Counters.Add(CounterType.Stun, 2); // Kaito's −2 places two.

        SeedLibrary(_alice, 12);

        var driver = NewDriver();

        // First untap step: removes one counter, still tapped.
        await driver.RunTurnAsync(_alice, turnNumber: 2);
        bear.IsTapped.Should().BeTrue();
        bear.Counters.Count(CounterType.Stun).Should().Be(1);

        // Second untap step: removes the last counter, still tapped.
        await driver.RunTurnAsync(_alice, turnNumber: 4);
        bear.IsTapped.Should().BeTrue();
        bear.Counters.Count(CounterType.Stun).Should().Be(0);

        // Third untap step: no counters left → finally untaps.
        await driver.RunTurnAsync(_alice, turnNumber: 6);
        bear.IsTapped.Should().BeFalse(
            "with no stun counters left the permanent untaps normally");
    }

    [Fact]
    public async Task NoStunCounter_UntapsNormally()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Tap();

        SeedLibrary(_alice, 5);
        var driver = NewDriver();
        await driver.RunTurnAsync(_alice, turnNumber: 2);

        bear.IsTapped.Should().BeFalse("no stun counter — normal untap");
    }
}
