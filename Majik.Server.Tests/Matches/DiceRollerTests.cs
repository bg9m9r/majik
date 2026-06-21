using FluentAssertions;
using Majik.Server.Matches;
using Xunit;

namespace Majik.Server.Tests.Matches;

public class DiceRollerTests
{
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
