using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Tests.Helpers; // BotTestScenario from Majik.Bot.Tests
using Xunit;

namespace Majik.Bot.Tests.Integration;

/// <summary>
/// Hard budget check on the bot's priority-decision latency. The bot must
/// stay well clear of any practical UI/SignalR turn timer.
/// </summary>
public class BotDecisionBudgetTests
{
    [Fact]
    public async Task PriorityDecisions_MeanUnder1500ms()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain");
        s.AddLandToBattlefield(s.Self, "Mountain");
        s.AddCreatureToBattlefield(s.Opponent, "Goblin", 1, 1);
        s.AddCreatureToBattlefield(s.Opponent, "Bear", 2, 2);

        var agent = new BotPlayerAgent(s.Self, new BotConfig("Burn"));
        var times = new List<long>();
        for (int i = 0; i < 100; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await agent.ChoosePriorityActionAsync(s.Context);
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
        }
        times.Average().Should().BeLessThan(1500);
        var p99 = times.OrderBy(t => t).ToList()[98];
        p99.Should().BeLessThan(3000);
    }
}
