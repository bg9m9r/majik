using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class CounterCancellationSBATests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);

    public CounterCancellationSBATests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public void PlusOnePlusOne_CancelsMinusOneMinusOne_RemovesPairs()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice, ActiveEffects = svc };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Counters.Add(CounterType.PlusOnePlusOne, 3);
        bear.Counters.Add(CounterType.MinusOneMinusOne, 2);

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { bear });

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
        bear.Power.Should().Be(3); // 2 base + 1 net counter
    }

    [Fact]
    public void MinusOneMinusOne_LethalToZeroToughness_KillsCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice, ActiveEffects = svc };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Counters.Add(CounterType.MinusOneMinusOne, 2);

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { bear });

        bear.Zone.Should().Be(ZoneType.Graveyard); // toughness 0 → dies
    }
}
