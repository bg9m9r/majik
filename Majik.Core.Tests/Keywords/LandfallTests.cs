using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class LandfallTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LandEnters_FiresLandfall_TriggerEnqueued()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var src = new Creature("Tatyova", "GU", 3, 3) { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var gained = 0;
        var landfall = LandfallFactory.Build(src, new IEffect[]
        {
            new Effect("gain 1", () => { _alice.GainLife(1); gained++; }),
        });
        src.AddAbility(landfall);
        triggers.BindCard(src);

        var mountain = NamedCardFactory.Create("Mountain", _alice);
        mountain.Zone = ZoneType.Hand;
        _alice.Zones.Hand.AddCard(mountain);
        zones.MoveCardTo(mountain, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(21);
        gained.Should().Be(1);
    }

    [Fact]
    public void NonLandEnters_DoesNotFireLandfall()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var src = new Creature("Tatyova", "GU", 3, 3) { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var landfall = LandfallFactory.Build(src, new IEffect[] { new Effect("noop", () => { }) });
        src.AddAbility(landfall);
        triggers.BindCard(src);

        var bear = NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.Zone = ZoneType.Hand;
        _alice.Zones.Hand.AddCard(bear);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0);
    }
}
