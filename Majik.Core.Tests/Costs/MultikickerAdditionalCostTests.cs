using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Tests for <see cref="MultikickerAdditionalCost"/> — CR 702.32 additive
/// optional cast cost paid any number of times. Covers the cost primitive's
/// affordability check (<see cref="MultikickerAdditionalCost.CanPay"/>), the
/// N-times payment + kick-count stamp
/// (<see cref="MultikickerAdditionalCost.Pay"/>), and the zero-kick / mana-
/// bounded edges.
/// </summary>
public class MultikickerAdditionalCostTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ManaCost Two => ManaCost.Parse("{2}");

    // ── Construction ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCard_Throws()
    {
        Action act = () => new MultikickerAdditionalCost(null!, Two, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCost_Throws()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        Action act = () => new MultikickerAdditionalCost(chalice, null!, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NegativeTimes_Throws()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        Action act = () => new MultikickerAdditionalCost(chalice, Two, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Description_ReflectsTimes()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        new MultikickerAdditionalCost(chalice, Two, 0).Description
            .Should().Contain("not paid");
        new MultikickerAdditionalCost(chalice, Two, 3).Description
            .Should().Contain("3");
    }

    // ── CanPay ───────────────────────────────────────────────────────────────

    [Fact]
    public void CanPay_ZeroTimes_AlwaysTrue_EvenWithEmptyPool()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        new MultikickerAdditionalCost(chalice, Two, 0).CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_TwoTimes_TrueWhenFourMana()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("{4}"));
        new MultikickerAdditionalCost(chalice, Two, 2).CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_TwoTimes_FalseWhenOnlyThreeMana()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("{3}"));
        new MultikickerAdditionalCost(chalice, Two, 2).CanPay(_alice).Should().BeFalse();
    }

    // ── Pay ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Pay_TwoTimes_DrainsFourMana_AndStampsTimesKicked2()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("{4}"));

        var cost = new MultikickerAdditionalCost(chalice, Two, 2);
        cost.Pay(_alice).Should().BeTrue();

        chalice.TimesKicked.Should().Be(2);
        chalice.WasKicked.Should().BeTrue();
        _alice.ManaPool.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Pay_ZeroTimes_PaysNothing_StampsZero()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);

        var cost = new MultikickerAdditionalCost(chalice, Two, 0);
        cost.Pay(_alice).Should().BeTrue();

        chalice.TimesKicked.Should().Be(0);
        chalice.WasKicked.Should().BeFalse();
    }

    [Fact]
    public void Pay_Unaffordable_ReturnsFalse_DoesNotStamp_DoesNotDrainPool()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("{3}"));

        var cost = new MultikickerAdditionalCost(chalice, Two, 2);
        cost.Pay(_alice).Should().BeFalse();

        // CR 601.2g — no partial payment, no leaked stamp.
        chalice.TimesKicked.Should().Be(0);
        chalice.WasKicked.Should().BeFalse();
        _alice.ManaPool.IsEmpty.Should().BeFalse();
    }
}
