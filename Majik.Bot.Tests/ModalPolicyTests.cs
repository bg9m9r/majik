using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Xunit;

namespace Majik.Bot.Tests;

public class ModalPolicyTests
{
    [Fact]
    public void PickMode_PrefersDrawOverGainLife()
    {
        // Bag-of-keywords scorer: "draw" (2.0) beats "gain ... life" (1.0).
        var s = new BotTestScenario();
        ModalPolicy.PickMode(s.Context, s.Self, new[] { "Draw 2", "Gain 5 life" }).Should().Be(0);
    }

    [Fact]
    public void PickMode_PrefersDestroyOverWeakerEffect()
    {
        var s = new BotTestScenario();
        var modes = new[] { "Gain 1 life.", "Destroy target creature." };
        ModalPolicy.PickMode(s.Context, s.Self, modes).Should().Be(1);
    }

    [Fact]
    public void PickMode_PenalisesSelfDrawback()
    {
        var s = new BotTestScenario();
        var modes = new[] { "You lose 3 life. Sacrifice a creature.", "Draw a card." };
        ModalPolicy.PickMode(s.Context, s.Self, modes).Should().Be(1);
    }

    [Fact]
    public void PickMode_DefaultsToFirst_WhenAllEqual()
    {
        var s = new BotTestScenario();
        var modes = new[] { "X.", "Y.", "Z." };
        ModalPolicy.PickMode(s.Context, s.Self, modes).Should().Be(0);
    }

    [Fact]
    public void PickMode_HandlesEmpty()
    {
        var s = new BotTestScenario();
        ModalPolicy.PickMode(s.Context, s.Self, System.Array.Empty<string>()).Should().Be(0);
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
