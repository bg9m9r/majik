using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Xunit;

namespace Majik.Bot.Tests;

public class CombatEvalTests
{
    [Fact]
    public void Score_RewardsOpponentLifeLoss()
    {
        var w = ArchetypeWeights.Burn;
        var noDamage = CombatEval.Score(0, 0, 0, 0, w);
        var oppLoses5 = CombatEval.Score(0, 5, 0, 0, w);
        oppLoses5.Should().BeGreaterThan(noDamage);
    }

    [Fact]
    public void Score_PenalizesBotLifeLoss()
    {
        var w = ArchetypeWeights.Burn;
        var noDamage = CombatEval.Score(0, 0, 0, 0, w);
        var botLoses5 = CombatEval.Score(5, 0, 0, 0, w);
        noDamage.Should().BeGreaterThan(botLoses5);
    }

    [Fact]
    public void Score_RewardsKillingOpponentCreatures()
    {
        var w = ArchetypeWeights.Prowess;
        var trade = CombatEval.Score(0, 0, 0, 4, w);
        trade.Should().BeGreaterThan(0);
    }
}
