using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Tests.Helpers;
using Xunit;

namespace Majik.Bot.Tests;

public class BotPlayerAgentTests
{
    [Fact]
    public async Task ChoosePriorityAction_ReturnsActionWithinBudget()
    {
        var s = new BotTestScenario();
        var agent = new BotPlayerAgent(s.Self, new BotConfig("Burn"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var action = await agent.ChoosePriorityActionAsync(s.Context);
        sw.Stop();
        action.Should().NotBeNull();
        sw.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    [Fact]
    public async Task RespectsCancellationToken()
    {
        var s = new BotTestScenario();
        var agent = new BotPlayerAgent(s.Self, new BotConfig("Burn"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await agent.ChoosePriorityActionAsync(s.Context, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
