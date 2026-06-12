using FluentAssertions;
using Majik.Core.Players;
using Xunit;

namespace Majik.Bot.Tests.Frozen;

/// <summary>
/// Wiring tests for the FB1 frozen-baseline strategy selector:
/// <c>BotConfig.Strategy = "frozen-fb1"</c> must install
/// <c>Majik.Bot.Frozen.FB1.HeuristicStrategy</c>, the existing
/// <c>"heuristic"</c> / <c>"mcts"</c> mappings must be untouched, and an
/// unknown strategy must still throw.
/// </summary>
public class FrozenStrategyWiringTests
{
    private static Player NewPlayer() => new("Bot", 20);

    [Fact]
    public void FrozenFb1_Strategy_Constructs_And_Installs_FB1_HeuristicStrategy()
    {
        var agent = new BotPlayerAgent(
            NewPlayer(), new BotConfig("Burn", Strategy: "frozen-fb1"));

        agent.InstalledStrategy.GetType().FullName
            .Should().Be("Majik.Bot.Frozen.FB1.HeuristicStrategy");
    }

    [Fact]
    public void Heuristic_Strategy_Still_Installs_Live_HeuristicStrategy()
    {
        var agent = new BotPlayerAgent(
            NewPlayer(), new BotConfig("Burn", Strategy: "heuristic"));

        agent.InstalledStrategy.GetType().FullName
            .Should().Be("Majik.Bot.Heuristic.HeuristicStrategy");
    }

    [Fact]
    public void Mcts_Strategy_Still_Installs_SearchStrategy()
    {
        var agent = new BotPlayerAgent(
            NewPlayer(), new BotConfig("Burn", Strategy: "mcts"));

        agent.InstalledStrategy.GetType().FullName
            .Should().Be("Majik.Bot.Search.SearchStrategy");
    }

    [Fact]
    public void Unknown_Strategy_Still_Throws()
    {
        var act = () => new BotPlayerAgent(
            NewPlayer(), new BotConfig("Burn", Strategy: "nosuchstrategy"));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown strategy*nosuchstrategy*");
    }
}
