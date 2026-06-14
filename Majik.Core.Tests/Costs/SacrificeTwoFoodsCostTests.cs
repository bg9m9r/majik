using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Tests for <see cref="SacrificeTwoFoodsCost"/> (CR 117 / CR 701.16) — the
/// payment-time "Sacrifice two Foods" activation cost used by
/// Asmoranomardicadaistinaculdacar's "Sacrifice two Foods: Target creature
/// deals 6 damage to itself." Sibling shape to
/// <see cref="SacrificeTwoArtifactsCost"/> narrowed to the Food subtype
/// (CR 205.3 — Food is an artifact subtype).
/// </summary>
public class SacrificeTwoFoodsCostTests
{
    private readonly Player _alice = new("Alice", 20);

    private Artifact Food(string name = "Food")
    {
        var a = new Artifact(name, "", subtypes: new[] { CardSubtype.Food })
        { Owner = _alice, Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(a);
        return a;
    }

    private Artifact PlainArtifact(string name)
    {
        var a = new Artifact(name, "{1}") { Owner = _alice, Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(a);
        return a;
    }

    [Fact]
    public void CanPay_FalseWithFewerThanTwoFoods()
    {
        new SacrificeTwoFoodsCost().CanPay(_alice).Should().BeFalse("zero Foods");
        Food();
        new SacrificeTwoFoodsCost().CanPay(_alice).Should().BeFalse("only one Food");
    }

    [Fact]
    public void CanPay_TrueWithTwoFoods()
    {
        Food(); Food();
        new SacrificeTwoFoodsCost().CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_IgnoresNonFoodArtifacts()
    {
        Food();
        PlainArtifact("Sol Ring");
        new SacrificeTwoFoodsCost().CanPay(_alice)
            .Should().BeFalse("a non-Food artifact does not count toward the cost (CR 205.3)");
    }

    [Fact]
    public void Pay_SacrificesTwoDistinctFoods_LeavingNonFoodArtifacts()
    {
        var fA = Food("Food A");
        var fB = Food("Food B");
        var sol = PlainArtifact("Sol Ring");

        var cost = new SacrificeTwoFoodsCost();
        cost.Pay(_alice);

        _alice.Zones.Battlefield.GetCards().Should().Contain(sol)
            .And.NotContain(fA).And.NotContain(fB);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fA).And.Contain(fB);
        fA.Zone.Should().Be(ZoneType.Graveyard);
        fB.Zone.Should().Be(ZoneType.Graveyard);
        cost.Targets.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Pay_ThrowsWhenFewerThanTwoFoods()
    {
        Food();
        var act = () => new SacrificeTwoFoodsCost().Pay(_alice);
        act.Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Pay_WithBus_PublishesPermanentSacrificedPerFood()
    {
        var fA = Food("Food A");
        var fB = Food("Food B");
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        new SacrificeTwoFoodsCost(bus).Pay(_alice);

        seen.Should().HaveCount(2, "CR 701.16a — one event per Food sacrificed");
        seen.Select(e => e.SacrificedCard).Should().BeEquivalentTo(new ICard[] { fA, fB });
        seen.Should().OnlyContain(e => ReferenceEquals(e.SacrificingPlayer, _alice));
    }

    [Fact]
    public void Pay_WithoutBus_DoesNotThrow_AndStillSacrifices()
    {
        var fA = Food("Food A");
        var fB = Food("Food B");
        new SacrificeTwoFoodsCost().Pay(_alice);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fA).And.Contain(fB);
    }
}
