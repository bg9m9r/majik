using FluentAssertions;
using Majik.Bot;
using Xunit;

namespace Majik.Bot.Tests;

public class BotConfigTests
{
    [Fact]
    public void Defaults_AreUsableWithoutOverrides()
    {
        var cfg = new BotConfig("Burn");
        cfg.ArchetypeName.Should().Be("Burn");
        cfg.SearchDepth.Should().Be(2);
        cfg.RandomSeed.Should().Be(0);
        cfg.Strategy.Should().Be("heuristic");
    }

    [Fact]
    public void Overrides_RoundTrip()
    {
        var cfg = new BotConfig("Prowess", SearchDepth: 3, RandomSeed: 42, Strategy: "mcts");
        cfg.SearchDepth.Should().Be(3);
        cfg.RandomSeed.Should().Be(42);
        cfg.Strategy.Should().Be("mcts");
    }
}
