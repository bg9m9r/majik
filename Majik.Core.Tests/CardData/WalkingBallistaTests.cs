using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>Unit tests for <see cref="WalkingBallistaFactory"/>.</summary>
public class WalkingBallistaTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_IsCreatureAndArtifact()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.HasType(CardType.Creature).Should().BeTrue("Walking Ballista is a Creature");
        wb.HasType(CardType.Artifact).Should().BeTrue("Walking Ballista is an Artifact");
    }

    [Fact]
    public void WalkingBallista_HasConstructSubtype()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    [Fact]
    public void WalkingBallista_IsZeroZero()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.BasePower.Should().Be(0);
        wb.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void WalkingBallista_OwnerAndControllerAreSet()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.Owner.Should().BeSameAs(_alice);
        wb.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability count / presence
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_HasExactlyTwoActivatedAbilities()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        wb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Grow ability: {4}: Put a +1/+1 counter (sorcery-speed restriction deferred)
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_GrowAbility_RequiresFourGenericMana()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        var grow = wb.Abilities.OfType<ActivatedAbility>()
            .FirstOrDefault(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Cost.TotalValue >= 4));

        grow.Should().NotBeNull("the {4} grow ability should be present");
    }

    [Fact]
    public void WalkingBallista_GrowAbility_AddsOnePlusOnePlusOneCounter_OnResolve()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        var grow = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Cost.TotalValue >= 4));

        grow.Resolve();

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Ping ability: Remove a +1/+1 counter: deal 1 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_PingAbility_HasRemoveCounterCost()
    {
        var wb = WalkingBallistaFactory.Create(_alice);

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .FirstOrDefault(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());

        ping.Should().NotBeNull("the ping ability should exist with a counter-removal cost");
    }

    [Fact]
    public void WalkingBallista_PingAbility_CannotPayWhenNoCounters()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        // starts at 0 counters

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.CanPay(_alice).Should().BeFalse("no counters to remove");
    }

    [Fact]
    public void WalkingBallista_PingAbility_CanPayWhenCounterPresent()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        wb.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void WalkingBallista_PingAbility_RemovesOneCounterOnPay()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        wb.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.Pay(_alice);

        wb.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void WalkingBallista_PingAbility_ThrowsWhenCounterMissing()
    {
        var wb = WalkingBallistaFactory.Create(_alice);
        // no counters

        var ping = wb.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        var act = () => cost.Pay(_alice);
        act.Should().Throw<InvalidOperationException>();
    }
}
