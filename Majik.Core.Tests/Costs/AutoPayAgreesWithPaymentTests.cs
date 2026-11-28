using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Auto-pay must agree with the real payment. CR 107.4c / 106.1b: a {C} pip
/// is paid only with colorless mana, so auto-select has to pick a colorless
/// source for it (Kozilek's Command, Glaring Fleshraker). And a source's mana
/// ability that cannot be activated right now must not be the one tapped
/// (CR 605.3a).
/// </summary>
public class AutoPayAgreesWithPaymentTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land Source(string name, params (string Mana, bool CanActivate)[] abilities)
    {
        var land = new Land(name);
        land.SetOwner(_alice); land.SetController(_alice);
        foreach (var (mana, can) in abilities)
        {
            var ok = can;
            land.AddAbility(new ManaAbility(land, _alice, ManaCost.Parse(mana),
                () => ok && !land.IsTapped));
        }
        _alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);
        return land;
    }

    [Fact]
    public void AColorlessPip_IsCoveredByAColorlessSource_EvenWhenColoredSourcesComeFirst()
    {
        Source("Forest A", ("{G}", true));
        Source("Forest B", ("{G}", true));
        var wastes = Source("Wastes", ("{C}", true));
        var resolver = new ManaPaymentResolver();
        var cost = ManaCost.Parse("{1}{C}");

        resolver.TryAutoSelectSources(_alice, cost, out var payment).Should().BeTrue();
        payment.Sources.Should().Contain(wastes);
        resolver.Pay(_alice, cost, payment).Should().BeTrue();
    }

    [Fact]
    public void AutoSelect_RefusesAColorlessPipNoSourceCanMake()
    {
        Source("Forest A", ("{G}", true));
        Source("Forest B", ("{G}", true));
        new ManaPaymentResolver().TryAutoSelectSources(_alice, ManaCost.Parse("{1}{C}"), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Pay_TapsAnAbilityThatCanBeActivated()
    {
        var land = Source("Odd Land", ("{R}", false), ("{R}", true));
        var resolver = new ManaPaymentResolver();

        var act = () => resolver.Pay(_alice, ManaCost.Parse("{R}"), new ManaPayment(new ICard[] { land }));

        act.Should().NotThrow();
        land.IsTapped.Should().BeTrue();
    }
}