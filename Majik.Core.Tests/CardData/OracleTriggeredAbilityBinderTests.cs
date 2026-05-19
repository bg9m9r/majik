using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class OracleTriggeredAbilityBinderTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ETB_GainLife_BindsAndFires()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var warden = new Creature("Soul Warden", "W", 1, 1) { Owner = _alice, Controller = _alice };
        warden.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(warden);

        var entity = new CardEntity
        {
            Name = "Soul Warden",
            OracleText = "When ~ enters the battlefield, you gain 1 life.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(warden, entity))
        {
            warden.AddAbility(ab);
        }
        triggers.BindCard(warden);

        zones.MoveCardTo(warden, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void ETB_DrawCards_BindsAndFires()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        for (var i = 0; i < 5; i++)
        {
            var c = NamedCardFactory.Create("Mountain", _alice);
            _alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library);
        }

        var mull = new Creature("Mulldrifter", "4U", 2, 2) { Owner = _alice, Controller = _alice };
        mull.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mull);

        var entity = new CardEntity
        {
            Name = "Mulldrifter",
            OracleText = "When ~ enters the battlefield, draw two cards.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(mull, entity))
        {
            mull.AddAbility(ab);
        }
        triggers.BindCard(mull);

        zones.MoveCardTo(mull, ZoneType.Battlefield, controller: _alice);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.Count.Should().Be(2);
    }

    [Fact]
    public void Dies_GainLife_BindsAndFires()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var creature = new Creature("Sad Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(creature);

        var entity = new CardEntity
        {
            Name = "Sad Bear",
            OracleText = "When ~ dies, you gain 2 life.",
        };
        foreach (var ab in OracleTriggeredAbilityBinder.Bind(creature, entity))
        {
            creature.AddAbility(ab);
        }
        triggers.BindCard(creature);

        // Simulate death: move to graveyard via fake event.
        _bus.Publish(new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(22);
    }

    [Fact]
    public void UnrecognisedText_NoBindings()
    {
        var creature = new Creature("Mystery", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice,
        };
        var entity = new CardEntity
        {
            Name = "Mystery",
            OracleText = "When ~ enters the battlefield, something weird happens.",
        };

        var bindings = OracleTriggeredAbilityBinder.Bind(creature, entity).ToList();

        // ETB pattern matches, but effect tail doesn't — produces no ability.
        bindings.Should().BeEmpty();
    }
}
