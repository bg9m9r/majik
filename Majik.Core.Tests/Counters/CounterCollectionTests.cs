using FluentAssertions;
using Majik.Core.Counters;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Counters;

public class CounterCollectionTests
{
    [Fact]
    public void Add_IncrementsCount()
    {
        var c = new CounterCollection();
        c.Add(CounterType.PlusOnePlusOne, 3);
        c.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    [Fact]
    public void Remove_DecrementsToZero_RemovesEntry()
    {
        var c = new CounterCollection();
        c.Add(CounterType.PlusOnePlusOne, 2);
        c.Remove(CounterType.PlusOnePlusOne, 5);
        c.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        c.HasAny.Should().BeFalse();
    }

    [Fact]
    public void DifferentTypes_TrackedSeparately()
    {
        var c = new CounterCollection();
        c.Add(CounterType.PlusOnePlusOne, 2);
        c.Add(CounterType.MinusOneMinusOne, 1);
        c.Add(CounterType.Charge, 4);

        c.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        c.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        c.Count(CounterType.Charge).Should().Be(4);
    }

    [Fact]
    public void Permanent_HasCounterCollection()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.Counters.Add(CounterType.PlusOnePlusOne, 2);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }
}
