using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Tests.Helpers;
using Xunit;

namespace Majik.Bot.Tests;

public class BoardEvalTests
{
    [Fact]
    public void Score_IncreasesWith_SelfLife()
    {
        var s1 = new BotTestScenario(selfLife: 10);
        var s2 = new BotTestScenario(selfLife: 20);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }

    [Fact]
    public void Score_DecreasesWith_OpponentLife()
    {
        var s1 = new BotTestScenario(oppLife: 10);
        var s2 = new BotTestScenario(oppLife: 20);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s1.Context, s1.Self, w).Should().BeGreaterThan(BoardEval.Score(s2.Context, s2.Self, w));
    }

    [Fact]
    public void Score_IncreasesWith_BotBoardPower()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddCreatureToBattlefield(s2.Self, "Grizzly Bears", 2, 2);
        var w = ArchetypeWeights.Prowess;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }

    [Fact]
    public void Score_DecreasesWith_OpponentBoardPower()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddCreatureToBattlefield(s2.Opponent, "Tarmogoyf", 4, 5);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s1.Context, s1.Self, w).Should().BeGreaterThan(BoardEval.Score(s2.Context, s2.Self, w));
    }

    [Fact]
    public void Score_IncreasesWith_ManaSources()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddLandToBattlefield(s2.Self, "Mountain");
        s2.AddLandToBattlefield(s2.Self, "Mountain");
        var w = ArchetypeWeights.BorosEnergy;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }
}
