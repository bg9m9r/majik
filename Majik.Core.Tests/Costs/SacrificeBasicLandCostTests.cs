using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 701.16 (sacrifice) — pays an additional cost by sacrificing a basic
/// land of a specific subtype. Used by Lava Dart's Flashback and any
/// future "Sacrifice a [basic-land-type]" cost.
/// </summary>
public class SacrificeBasicLandCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CanPay_OwnedControlledMountainOnBattlefield_True()
    {
        var mountain = MountainOnBattlefield(_alice);
        var cost = new SacrificeBasicLandCost(mountain, CardSubtype.Mountain);

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_WrongSubtype_False()
    {
        // Plains can't pay a "sacrifice a Mountain" cost.
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        plains.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(plains);

        var cost = new SacrificeBasicLandCost(plains, CardSubtype.Mountain);
        cost.CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_ControlledByOpponent_False()
    {
        var mountain = MountainOnBattlefield(_bob);
        var cost = new SacrificeBasicLandCost(mountain, CardSubtype.Mountain);

        cost.CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_NotOnBattlefield_False()
    {
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        mountain.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mountain);

        var cost = new SacrificeBasicLandCost(mountain, CardSubtype.Mountain);
        cost.CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void Pay_MovesLandFromBattlefieldToGraveyard()
    {
        var mountain = MountainOnBattlefield(_alice);
        var cost = new SacrificeBasicLandCost(mountain, CardSubtype.Mountain);

        cost.Pay(_alice).Should().BeTrue();
        mountain.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mountain);
        _alice.Zones.Graveyard.GetCards().Should().Contain(mountain);
    }

    [Fact]
    public void Pay_WhenCantPay_ReturnsFalseAndLandRemains()
    {
        // Mountain controlled by opponent — Alice can't sacrifice it.
        var mountain = MountainOnBattlefield(_bob);
        var cost = new SacrificeBasicLandCost(mountain, CardSubtype.Mountain);

        cost.Pay(_alice).Should().BeFalse();
        mountain.Zone.Should().Be(ZoneType.Battlefield);
    }

    private static Land MountainOnBattlefield(Player owner)
    {
        var m = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        m.SetOwner(owner);
        m.SetController(owner);
        m.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(m);
        return m;
    }
}
