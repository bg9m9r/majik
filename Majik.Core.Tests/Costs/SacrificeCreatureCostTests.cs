using FluentAssertions;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class SacrificeCreatureCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Pay_MovesCreature_ToGraveyard()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);
        var cost = new SacrificeCreatureCost(bear);

        cost.Pay(_alice).Should().BeTrue();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void Pay_OpponentControlled_Fails()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        _bob.Zones.Battlefield.AddCard(bear);
        var cost = new SacrificeCreatureCost(bear);

        cost.Pay(_alice).Should().BeFalse();
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Pay_NotOnBattlefield_Fails()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Graveyard };
        var cost = new SacrificeCreatureCost(bear);

        cost.Pay(_alice).Should().BeFalse();
    }
}
