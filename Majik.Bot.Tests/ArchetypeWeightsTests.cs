using FluentAssertions;
using Majik.Bot.Decks;
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
    public void Untuned_FallsBackToNeutralDefault()
    {
        // An archetype without a bespoke table must NOT throw — it is still a
        // selectable bot (see ForArchetype docs). It resolves to the neutral
        // Default baseline.
        ArchetypeWeights.ForArchetype("Mystery").Should().BeSameAs(ArchetypeWeights.Default);
    }

    [Fact]
    public void EveryCatalogArchetype_ResolvesToWeights_WithoutThrowing()
    {
        // Regression guard: every archetype surfaced to the bot picker
        // (GET /matches/archetypes) and accepted by MatchService must resolve
        // to a usable weight table, or selecting that bot crashes match
        // creation in HeuristicStrategy's ctor.
        foreach (var archetype in BotDeckCatalog.Archetypes)
        {
            var act = () => ArchetypeWeights.ForArchetype(archetype);
            act.Should().NotThrow($"'{archetype}' is a selectable bot archetype");
        }
    }
}
