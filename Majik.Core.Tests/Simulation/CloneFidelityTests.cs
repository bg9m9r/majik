using FluentAssertions;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.ValueObjects;
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

    [Fact]
    public void Clone_CopiesPlayerScalarState()
    {
        var alice = new Player("Alice", 20);
        alice.GainEnergy(3);                              // EnergyCounters
        alice.AddPoisonCounters(2);                       // PoisonCounters
        alice.AddManaToPool(ManaCost.Parse("{R}{R}"));    // _manaPool (Red = 2)

        var cloned = GameStateCloner.Clone(new[] { alice });
        var c = cloned.PlayerFor(alice);

        c.EnergyCounters.Should().Be(3);
        c.PoisonCounters.Should().Be(2);
        c.ManaPool.Red.Should().Be(2);

        // Independence: mutating the clone must not touch the original.
        c.GainEnergy(10);
        alice.EnergyCounters.Should().Be(3);
    }
}
