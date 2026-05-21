using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Xunit;

namespace Majik.Bot.Tests;

public class ModalPolicyTests
{
    [Fact]
    public void PickMode_DefaultsToFirst()
    {
        var s = new BotTestScenario();
        ModalPolicy.PickMode(s.Context, s.Self, new[] { "Draw 2", "Gain 5 life" }).Should().Be(0);
    }

    [Fact]
    public void PickX_AllInOnAvailableMana()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        s.AddLandToBattlefield(s.Self, "Mountain3");
        ModalPolicy.PickX(s.Context, s.Self).Should().Be(3);
    }
}
