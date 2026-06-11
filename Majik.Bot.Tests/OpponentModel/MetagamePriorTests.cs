using System.Linq;
using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Bot.OpponentModel;
using Xunit;

namespace Majik.Bot.Tests.OpponentModel;

public class MetagamePriorTests
{
    [Fact]
    public void Weights_CoverEveryArchetype_AreNormalized_AndPositive()
    {
        var names = BotDeckCatalog.Archetypes.ToList();
        foreach (var a in names)
            MetagamePrior.Weight(a).Should().BeGreaterThan(0, $"every archetype needs a prior: {a}");
        names.Sum(a => MetagamePrior.Weight(a)).Should().BeApproximately(1.0, 1e-9);
        MetagamePrior.Weight("NotARealArchetype").Should().Be(0.0);
    }
}
