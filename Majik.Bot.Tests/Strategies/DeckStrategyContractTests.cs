using FluentAssertions;
using Majik.Bot.Strategies;
using Xunit;

namespace Majik.Bot.Tests.Strategies;

public sealed class DeckStrategyContractTests
{
    [Fact]
    public void Attribute_CarriesDeckName()
    {
        var attr = new DeckStrategyAttribute("GrixisReanimator");
        attr.DeckName.Should().Be("GrixisReanimator");
    }
}
