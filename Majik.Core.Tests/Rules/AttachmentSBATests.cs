using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Enchantment = Majik.Core.Cards.Enchantment;

public class AttachmentSBATests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);

    public AttachmentSBATests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public void Aura_BearerLeavesBattlefield_GoesToGraveyard()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var aura = new Enchantment("Holy Strength", "W",
            subtypes: new[] { CardSubtype.Aura }) { Owner = _alice, Controller = _alice };
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.AttachTo(bear);

        // Bearer dies.
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(bear);
        _alice.Zones.Graveyard.AddCard(bear);

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { aura, bear });

        aura.Zone.Should().Be(ZoneType.Graveyard);
        aura.AttachedTo.Should().BeNull();
    }

    [Fact]
    public void Equipment_BearerLeavesBattlefield_UnattachesButStays()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var sword = new Artifact("Sword", "2",
            subtypes: new[] { CardSubtype.Equipment }) { Owner = _alice, Controller = _alice };
        sword.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.AttachTo(bear);

        // Bearer dies.
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(bear);
        _alice.Zones.Graveyard.AddCard(bear);

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { sword, bear });

        sword.Zone.Should().Be(ZoneType.Battlefield); // stays
        sword.AttachedTo.Should().BeNull(); // unattaches
    }

    [Fact]
    public void Aura_StillLegallyAttached_NoSBAAction()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var aura = new Enchantment("Aura", "W",
            subtypes: new[] { CardSubtype.Aura }) { Owner = _alice, Controller = _alice };
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.AttachTo(bear);

        _sba.CheckStateBasedActions(new[] { _alice }, new ICard[] { aura, bear });

        aura.Zone.Should().Be(ZoneType.Battlefield);
        aura.AttachedTo.Should().BeSameAs(bear);
    }
}
