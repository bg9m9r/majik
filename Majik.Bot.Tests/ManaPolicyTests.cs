using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Xunit;

namespace Majik.Bot.Tests;

public class ManaPolicyTests
{
    [Fact]
    public void Returns_Empty_WhenNoManaSourcesNeeded()
    {
        var s = new BotTestScenario();
        var payment = ManaPolicy.Pick(s.Context, s.Self, costGenericAmount: 0, coloredRequired: 0);
        payment.Sources.Should().BeEmpty();
    }
}
