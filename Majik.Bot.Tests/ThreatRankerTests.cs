using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Tests.Helpers;
using Xunit;

namespace Majik.Bot.Tests;

public class ThreatRankerTests
{
    [Fact]
    public void Ranks_HighestPowerFirst()
    {
        var s = new BotTestScenario();
        var small = s.AddCreatureToBattlefield(s.Opponent, "Goblin", 1, 1);
        var big   = s.AddCreatureToBattlefield(s.Opponent, "Wurm", 6, 6);
        var med   = s.AddCreatureToBattlefield(s.Opponent, "Bear", 2, 2);

        var ranked = ThreatRanker.Rank(s.Opponent).ToList();
        ranked[0].Should().BeSameAs(big);
        ranked[1].Should().BeSameAs(med);
        ranked[2].Should().BeSameAs(small);
    }

    [Fact]
    public void EmptyBoard_ReturnsEmpty()
    {
        var s = new BotTestScenario();
        ThreatRanker.Rank(s.Opponent).Should().BeEmpty();
    }
}
