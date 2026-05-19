using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

public class BuybackOverloadTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Buyback_PaysMana_OnSuccess()
    {
        _alice.AddManaToPool(ManaCost.Parse("2"));
        var card = new Instant("Capsize", "1U") { Owner = _alice, Zone = ZoneType.Stack };
        var bb = new BuybackAdditionalCost(card, ManaCost.Parse("2"));

        bb.Pay(_alice).Should().BeTrue();
        _alice.ManaPool.Generic.Should().Be(0);
    }

    [Fact]
    public void Buyback_InsufficientMana_Fails()
    {
        var card = new Instant("Capsize", "1U") { Owner = _alice, Zone = ZoneType.Stack };
        var bb = new BuybackAdditionalCost(card, ManaCost.Parse("2"));

        bb.Pay(_alice).Should().BeFalse();
    }

    [Fact]
    public void Buyback_ReturnOnResolve_PutsCardFromStackToHand()
    {
        var card = new Instant("Capsize", "1U") { Owner = _alice, Zone = ZoneType.Stack };
        var bb = new BuybackAdditionalCost(card, ManaCost.Parse("2"));

        bb.ReturnOnResolve(_alice);

        card.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(card);
    }

    [Fact]
    public void Overload_CanCastFromHand_OwnedBySelf()
    {
        var card = new Instant("Mizzium Mortars", "1R") { Owner = _alice, Zone = ZoneType.Hand };
        var ov = new OverloadAlternativeCost(ManaCost.Parse("4RR"));

        ov.CanCastFor(card, _alice).Should().BeTrue();
        ov.AlternativeManaCost.Should().Be(ManaCost.Parse("4RR"));
    }

    [Fact]
    public void Overload_IsOverloaded_FlippedTrueAfterOnResolved()
    {
        var card = new Instant("Mizzium Mortars", "1R") { Owner = _alice, Zone = ZoneType.Hand };
        var ov = new OverloadAlternativeCost(ManaCost.Parse("4RR"));

        ov.IsOverloaded.Should().BeFalse();
        ov.OnResolved(card, _alice);
        ov.IsOverloaded.Should().BeTrue();
    }
}
