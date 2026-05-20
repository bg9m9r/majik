using FluentAssertions;
using Majik.Server.Matches;
using Xunit;

#pragma warning disable CS0618 // Roll(string, string) is obsolete — these tests cover legacy behaviour

namespace Majik.Server.Tests.Matches;

public class DiceRollerTests
{
    private sealed class StubRandomSource : IRandomSource
    {
        private readonly Queue<int> _values;
        public StubRandomSource(params int[] values) => _values = new Queue<int>(values);
        public int NextInt(int minInclusive, int maxExclusive) => _values.Dequeue();
    }

    [Fact]
    public void Roll_CreatorHigher_WinsForCreator()
    {
        var rng = new StubRandomSource(6, 2);
        var roller = new DiceRoller(rng);

        var roll = roller.Roll("alice", "bob");

        roll.CreatorRoll.Should().Be(6);
        roll.OpponentRoll.Should().Be(2);
        roll.WinnerSub.Should().Be("alice");
    }

    [Fact]
    public void Roll_OpponentHigher_WinsForOpponent()
    {
        var rng = new StubRandomSource(1, 4);
        var roller = new DiceRoller(rng);

        var roll = roller.Roll("alice", "bob");

        roll.WinnerSub.Should().Be("bob");
    }

    [Fact]
    public void Roll_TiesRetryUntilDifferent()
    {
        var rng = new StubRandomSource(3, 3, 5, 5, 2, 6);
        var roller = new DiceRoller(rng);

        var roll = roller.Roll("alice", "bob");

        roll.CreatorRoll.Should().Be(2);
        roll.OpponentRoll.Should().Be(6);
        roll.WinnerSub.Should().Be("bob");
    }

    [Fact]
    public void SystemRandomSource_StaysWithinRange()
    {
        var rng = new SystemRandomSource();
        for (var i = 0; i < 200; i++)
        {
            var n = rng.NextInt(1, 7);
            n.Should().BeInRange(1, 6);
        }
    }
}
