using FluentAssertions;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class WardTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void OpponentCasts_DidNotPay_Counters()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, ManaCost.Parse("2"));

        ward.ResolvesWard(_alice, casterPaidWardCost: false).Should().BeTrue();
    }

    [Fact]
    public void OpponentCasts_Paid_NoCounter()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, ManaCost.Parse("2"));

        ward.ResolvesWard(_alice, casterPaidWardCost: true).Should().BeFalse();
    }

    [Fact]
    public void OwnControllerTargets_DoesNotTrigger()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var ward = new WardEffect(bear, ManaCost.Parse("2"));

        ward.ResolvesWard(_bob, casterPaidWardCost: false).Should().BeFalse();
    }
}
