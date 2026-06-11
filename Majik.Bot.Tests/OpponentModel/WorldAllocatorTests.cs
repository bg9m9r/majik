using FluentAssertions;
using Majik.Bot.OpponentModel;
using Xunit;

namespace Majik.Bot.Tests.OpponentModel;

public class WorldAllocatorTests
{
    private static ArchetypeWeight W(string a, double w) => new(a, w);

    [Fact]
    public void LargestRemainder_SumsToK_AndIsProportional()
    {
        var belief = new[] { W("Burn", .55), W("Prowess", .30), W("Boros", .15) };
        var alloc = WorldAllocator.Allocate(belief, k: 4, topM: 4);
        alloc.Sum(x => x.Worlds).Should().Be(4);
        alloc.Single(x => x.Archetype == "Burn").Worlds.Should().Be(2);
        alloc.Single(x => x.Archetype == "Prowess").Worlds.Should().Be(1);
        alloc.Single(x => x.Archetype == "Boros").Worlds.Should().Be(1);
    }

    [Fact]
    public void TopM_DropsTheTail()
    {
        var belief = new[] { W("A", .4), W("B", .3), W("C", .2), W("D", .07), W("E", .03) };
        var alloc = WorldAllocator.Allocate(belief, k: 4, topM: 3);
        alloc.Select(x => x.Archetype).Should().BeSubsetOf(new[] { "A", "B", "C" });
        alloc.Sum(x => x.Worlds).Should().Be(4);
    }

    [Fact]
    public void K1_GivesTheSingleTopArchetypeOneWorld()
    {
        var belief = new[] { W("A", .6), W("B", .4) };
        var alloc = WorldAllocator.Allocate(belief, k: 1, topM: 4);
        alloc.Sum(x => x.Worlds).Should().Be(1);
        alloc.Single(x => x.Worlds == 1).Archetype.Should().Be("A");
    }

    [Fact]
    public void KZero_ReturnsEmpty()
    {
        var belief = new[] { W("A", 1.0) };
        WorldAllocator.Allocate(belief, k: 0, topM: 4).Should().BeEmpty();
    }
}
