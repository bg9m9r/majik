using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

public class ManaPaymentResolverTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Pay_TapsRequestedSources_AddsManaToPool_DeductsCost()
    {
        var mountain1 = NamedCardFactory.Create("Mountain", _alice);
        var mountain2 = NamedCardFactory.Create("Mountain", _alice);
        mountain1.SetZone(ZoneType.Battlefield);
        mountain2.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("1R"),
            new ManaPayment(new ICard[] { mountain1, mountain2 }));

        success.Should().BeTrue();
        ((Permanent)mountain1).IsTapped.Should().BeTrue();
        ((Permanent)mountain2).IsTapped.Should().BeTrue();
        _alice.ManaPool.Total.Should().Be(0); // both tapped → 2 mana, all spent
    }

    [Fact]
    public void Pay_InsufficientMana_ReturnsFalse_SourcesNotTapped()
    {
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        mountain.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("1R"),
            new ManaPayment(new[] { mountain }));

        success.Should().BeFalse();
        ((Permanent)mountain).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Pay_NonManaSource_Throws()
    {
        var bear = NamedCardFactory.Create("Grizzly Bears", _alice);
        var resolver = new ManaPaymentResolver();

        var act = () => resolver.Pay(_alice, ManaCost.Parse("R"),
            new ManaPayment(new[] { bear }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mana ability*");
    }
}
