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

    [Fact]
    public async Task ChoosePriorityAction_FiresOnThinkingCallback_TrueThenFalse()
    {
        var s = new BotTestScenario();
        var calls = new List<bool>();
        var agent = new BotPlayerAgent(s.Self, new BotConfig("Burn"), thinking => calls.Add(thinking));
        await agent.ChoosePriorityActionAsync(s.Context);
        calls.Should().Equal(new[] { true, false });
    }

    // CR 614.12 / CR 201.4 — "choose a card name": the bot names the top-ranked
    // suggested threat (the engine hands the pool most-threatening-first), and
    // falls back to the supplied fallback when no threats are visible.
    [Fact]
    public async Task ChooseCardName_NamesTopSuggestedThreat()
    {
        var s = new BotTestScenario();
        var agent = new BotPlayerAgent(s.Self, new BotConfig("Burn"));

        var name = await agent.ChooseCardNameAsync(
            s.Context,
            suggested: new[] { "Griselbrand", "Tarmogoyf" },
            constraintLabel: "a card name");

        name.Should().Be("Griselbrand");
    }

    [Fact]
    public async Task ChooseCardName_NoSuggestions_ReturnsFallback()
    {
        var s = new BotTestScenario();
        var agent = new BotPlayerAgent(s.Self, new BotConfig("Burn"));

        var name = await agent.ChooseCardNameAsync(
            s.Context,
            suggested: System.Array.Empty<string>(),
            constraintLabel: "a card name",
            fallback: "");

        name.Should().BeEmpty();
    }
}
