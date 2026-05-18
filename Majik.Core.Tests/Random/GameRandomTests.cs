using FluentAssertions;
using Majik.Core.Random;
using Xunit;

public class GameRandomTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new GameRandom(42);
        var b = new GameRandom(42);

        var seqA = Enumerable.Range(0, 10).Select(_ => a.Next(100)).ToList();
        var seqB = Enumerable.Range(0, 10).Select(_ => b.Next(100)).ToList();

        seqA.Should().Equal(seqB);
    }

    [Fact]
    public void Shuffle_IsDeterministic_WithSameSeed()
    {
        var rng1 = new GameRandom(7);
        var rng2 = new GameRandom(7);
        var listA = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var listB = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        rng1.Shuffle(listA);
        rng2.Shuffle(listB);

        listA.Should().Equal(listB);
    }

    [Fact]
    public void Shuffle_AllElementsPresent()
    {
        var rng = new GameRandom(1);
        var list = Enumerable.Range(0, 60).ToList();
        var original = list.ToList();

        rng.Shuffle(list);

        list.OrderBy(x => x).Should().Equal(original);
    }

    [Fact]
    public void RollDie_InRange()
    {
        var rng = new GameRandom(99);
        for (var i = 0; i < 100; i++)
        {
            var r = rng.RollDie(6);
            r.Should().BeInRange(1, 6);
        }
    }

    [Fact]
    public void RollDie_InvalidSides_Throws()
    {
        var rng = new GameRandom();
        var act = () => rng.RollDie(0);
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FlipCoin_AbsentSeed_StillRunsWithoutError()
    {
        var rng = new GameRandom();
        for (var i = 0; i < 10; i++) _ = rng.FlipCoin();
    }
}
