using FluentAssertions;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Xunit;

namespace Majik.Core.Tests.Simulation;

public sealed class CloneFidelityTests
{
    [Fact]
    public void Clone_CopiesLife_AndIsIndependent()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 17);

        var cloned = GameStateCloner.Clone(new[] { alice, bob });

        var cAlice = cloned.PlayerFor(alice);
        var cBob = cloned.PlayerFor(bob);
        cAlice.LifeTotal.Should().Be(20);
        cBob.LifeTotal.Should().Be(17);

        // Independence: mutating the clone must not touch the original.
        cAlice.SetLifeTotal(5);
        alice.LifeTotal.Should().Be(20);
    }
}
