using FluentAssertions;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class CounterPTTests
{
    [Fact]
    public void PlusOnePlusOne_BoostsPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { ActiveEffects = svc };
        bear.Counters.Add(CounterType.PlusOnePlusOne, 3);

        bear.Power.Should().Be(5);
        bear.Toughness.Should().Be(5);
    }

    [Fact]
    public void MinusOneMinusOne_ReducesPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { ActiveEffects = svc };
        bear.Counters.Add(CounterType.MinusOneMinusOne, 1);

        bear.Power.Should().Be(1);
        bear.Toughness.Should().Be(1);
    }

    [Fact]
    public void BothCounters_NetEffectApplied_BeforeSBACancellation()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { ActiveEffects = svc };
        bear.Counters.Add(CounterType.PlusOnePlusOne, 3);
        bear.Counters.Add(CounterType.MinusOneMinusOne, 1);

        // Net +2/+2 before cancellation. SBA pairs them off separately.
        bear.Power.Should().Be(4);
        bear.Toughness.Should().Be(4);
    }
}
