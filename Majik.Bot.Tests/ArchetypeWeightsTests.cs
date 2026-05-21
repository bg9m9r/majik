using FluentAssertions;
using Majik.Bot.Evaluation;
using Xunit;

namespace Majik.Bot.Tests;

public class ArchetypeWeightsTests
{
    [Fact]
    public void Burn_PrioritizesOpponentLifeLoss()
    {
        var w = ArchetypeWeights.ForArchetype("Burn");
        w.LifeDelta.Should().BeGreaterThan(w.BoardPower); // race plan
    }

    [Fact]
    public void Prowess_PrioritizesBoardPower()
    {
        var w = ArchetypeWeights.ForArchetype("Prowess");
        w.BoardPower.Should().BeGreaterThan(w.HandSize);
    }

    [Fact]
    public void BorosEnergy_PrioritizesCardAdvantage()
    {
        var w = ArchetypeWeights.ForArchetype("BorosEnergy");
        w.HandSize.Should().BeGreaterThan(w.LifeDelta);
    }

    [Fact]
    public void Unknown_Throws()
    {
        var act = () => ArchetypeWeights.ForArchetype("Mystery");
        act.Should().Throw<ArgumentException>().WithMessage("*Mystery*");
    }
}
